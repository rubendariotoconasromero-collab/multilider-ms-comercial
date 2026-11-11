using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.DTOs;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Services;
public sealed record CoordinatedSale(Guid OperacionId,Guid CommandId,int ClienteId,int AsesorId,int PropiedadId,DateOnly Fecha,
    string TipoVenta,decimal MontoTotal,decimal? MontoLiquido=null,decimal Descuento=0,decimal CuotaInicial=0,
    int NumeroCuotas=0,decimal TasaInteres=0,decimal TasaMora=0,DateOnly? FechaInicioPago=null,string? ExpectedVersion=null);
public sealed record CancelSale(Guid OperacionId,Guid CommandId);

public interface IOperationObserver { void Boundary(Guid operation,string step,bool after); }
public sealed class SaleCoordinator(AppDbContext db,OperationChannel channel,IConfiguration config,IOperationObserver? observer=null) {
    private void Enabled() {
        Api.Require(config.GetValue<bool>("Operations:WorkerEnabled") && !string.IsNullOrWhiteSpace(config["Operations:Dataset"]),
            "INTEGRATION_NOT_READY: configura el coordinador y su worker.",503);
    }
    public static void Own(SaleOperation op,ClaimsPrincipal actor)=>Api.Require(op.ActorIssuer==actor.FindFirstValue("iss")&&op.ActorSubject==actor.FindFirstValue("sub"),"Operación ajena.",403);
    public async Task<SaleOperation> Begin(CoordinatedSale r,ClaimsPrincipal actor,string bearer,CancellationToken ct) {
        Enabled();
        Api.Require(r.OperacionId!=Guid.Empty&&r.CommandId!=Guid.Empty&&r.Fecha!=default&&r.TipoVenta is "CONTADO" or "CREDITO","Datos de venta inválidos.");
        var payload=JsonSerializer.Serialize(r,OperationProtocol.Json);var hash=OperationProtocol.Hash(r);
        await using(var tx=await db.Database.BeginTransactionAsync(ct)) {
            await OperationProtocol.Lock(db,"commercial:operations",ct);
            var op=await db.Set<SaleOperation>().FindAsync([r.OperacionId],ct);
            if(op is not null){Own(op,actor);Api.Require(op.PayloadHash==hash&&op.CommandId==r.CommandId,"UUID usado con otros datos.",409);}
            else {
                Api.Require((await Api.Find(db.Clientes,r.ClienteId,ct)).Estado&&(await Api.Find(db.Asesores,r.AsesorId,ct)).Estado,"Cliente o asesor inactivo.");
                op=new SaleOperation{Id=r.OperacionId,CommandId=r.CommandId,PropiedadId=r.PropiedadId,Payload=payload,PayloadHash=hash,
                    ActorIssuer=actor.FindFirstValue("iss")!,ActorSubject=actor.FindFirstValue("sub")!};
                db.Add(op);
                var intent=await db.AvailabilityIntents.FindAsync([r.OperacionId],ct);
                if(intent is not null)Api.Require(intent.ActorIssuer==op.ActorIssuer&&intent.ActorSubject==op.ActorSubject&&intent.PropiedadId==r.PropiedadId&&intent.ClienteId==r.ClienteId&&intent.AsesorId==r.AsesorId,"Intención de reserva incompatible.",409);
                else db.AvailabilityIntents.Add(new AvailabilityIntent{Id=op.Id,ActorIssuer=op.ActorIssuer,ActorSubject=op.ActorSubject,
                    PropiedadId=r.PropiedadId,ClienteId=r.ClienteId,AsesorId=r.AsesorId});
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        var result=await db.Set<SaleOperation>().FindAsync([r.OperacionId],ct);
        if(result!.Step=="Admitiendo"||result.ErrorCode=="REAUTHORIZATION_REQUIRED") {
            try {
                await channel.Send("seguridad",HttpMethod.Post,"api/auth/operaciones",new OperationAdmission(result.Id,"venta",r.PropiedadId,hash),ct,bearer);
                result=await AdmissionResult(result.Id,"Oferta",null,ct);
            } catch(Exception e) when(e is ProtocolException or HttpRequestException or TaskCanceledException) {
                result=await AdmissionResult(result.Id,"Oferta",e is ProtocolException {Status:401 or 403}?"REAUTHORIZATION_REQUIRED":"ADMISSION_UNCERTAIN",CancellationToken.None);
            }
        }
        return result;
    }
    public async Task<SaleOperation> Cancel(int venta,CancelSale r,ClaimsPrincipal actor,string bearer,CancellationToken ct) {
        Enabled();Api.Require(r.OperacionId!=Guid.Empty&&r.CommandId!=Guid.Empty,"Usa UUID válidos.");
        SaleOperation result;
        await using(var tx=await db.Database.BeginTransactionAsync(ct)) {
            await OperationProtocol.Lock(db,"commercial:operations",ct);
            var parent=await db.Set<SaleOperation>().SingleOrDefaultAsync(x=>x.Kind=="venta"&&x.VentaId==venta,ct)??throw new BusinessException("Venta no coordinada; requiere migración.",409);
            var hash=OperationProtocol.Hash(new{venta,parent=parent.Id,r});
            result=(await db.Set<SaleOperation>().FindAsync([r.OperacionId],ct))!;
            if(result is not null){Own(result,actor);Api.Require(result.Kind=="anulacion"&&result.PayloadHash==hash&&result.CommandId==r.CommandId,"UUID incompatible.",409);}
            else {
                Api.Require(parent.State=="Completada","La venta no está completada.",409);
                var sale=await Api.Find(db.Ventas,venta,ct);
                Api.Require(sale.Estado=="Activa"&&!await db.Entregas.AnyAsync(x=>x.Contrato.NotaVentaId==venta&&x.Estado=="Entregado",ct),"Venta anulada o entregada.",409);
                Api.Require(!await db.Set<SaleOperation>().AnyAsync(x=>x.ParentId==parent.Id&&x.Kind=="anulacion"&&x.State!="Rechazada",ct),"La venta ya tiene una anulación en curso.",409);
                result=new SaleOperation{Id=r.OperacionId,CommandId=r.CommandId,Kind="anulacion",ParentId=parent.Id,VentaId=venta,
                    PropiedadId=parent.PropiedadId,Payload=JsonSerializer.Serialize(r,OperationProtocol.Json),PayloadHash=hash,
                    ActorIssuer=actor.FindFirstValue("iss")!,ActorSubject=actor.FindFirstValue("sub")!,Reservation=parent.Reservation};
                db.Add(result);sale.Estado="Anulando";await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        if(result.Step=="Admitiendo"||result.ErrorCode=="REAUTHORIZATION_REQUIRED") {
            try{await channel.Send("seguridad",HttpMethod.Post,"api/auth/operaciones",new OperationAdmission(result.Id,"anulacion",result.PropiedadId,result.PayloadHash,result.ParentId),ct,bearer);
                result=await AdmissionResult(result.Id,"AutorizarAnulacion",null,ct);}
            catch(Exception e)when(e is ProtocolException or HttpRequestException or TaskCanceledException){result=await AdmissionResult(result.Id,"AutorizarAnulacion","ADMISSION_UNCERTAIN",CancellationToken.None);}
        }
        return result;
    }
    private async Task<SaleOperation> AdmissionResult(Guid id,string next,string? error,CancellationToken ct) {
        // The worker can claim an intent while the user request is admitting it in Seguridad.
        // Refresh under the same SQL mutex; never save the caller's stale lease/stamp over a worker.
        db.ChangeTracker.Clear();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        var row=await db.Set<SaleOperation>().SingleAsync(x=>x.Id==id,ct);
        if(row.State=="Pendiente"&&(row.LeaseOwner is null||row.LeaseUntil<=DateTime.UtcNow)) {
            if(row.Step=="Admitiendo"&&error is null)row.Step=next;
            row.ErrorCode=error;row.NextAttempt=DateTime.UtcNow;row.Stamp=Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);return row;
    }
    public async Task<bool> Run(CancellationToken ct) {
        Enabled();db.ChangeTracker.Clear();SaleOperation? op;var lease=Guid.NewGuid();
        await using(var tx=await db.Database.BeginTransactionAsync(ct)) {
            await OperationProtocol.Lock(db,"commercial:operations",ct);
            var now=DateTime.UtcNow;
            op=await db.Set<SaleOperation>().Where(x=>x.State=="Pendiente"&&x.NextAttempt<=now&&(x.LeaseUntil==null||x.LeaseUntil<now))
                .OrderBy(x=>x.NextAttempt).FirstOrDefaultAsync(ct);
            if(op is null)return false;
            op.LeaseOwner=lease;op.LeaseUntil=now.AddSeconds(45);op.Stamp=Guid.NewGuid();await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        string next=op.Step;object? receipt=null;string? error=null;bool terminal=false;
        observer?.Boundary(op.Id,op.Step,false);
        try {
            switch(op.Step) {
                case "Admitiendo":
                    await channel.Check(op.Id,"read",ct);next=op.Kind=="venta"?"Oferta":"AutorizarAnulacion";break;
                case "Oferta":
                    // Reuse an existing F4 hold if this intention already owns one.
                    try{receipt=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/reserva",null,ct);
                        var held=(JsonElement)receipt;
                        Api.Require(held.GetProperty("estado_reserva").GetString()=="Retenida","Reserva previa incompatible.",409);
                        op.Reservation=held.GetRawText();next="PrepararVenta";}
                    catch(ProtocolException e)when(e.Status==404) {
                        receipt=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/oferta",null,ct);
                        var offer=(JsonElement)receipt;var input=Input(op);
                        Api.Require(input.ExpectedVersion is null||input.ExpectedVersion==offer.GetProperty("version").GetString(),"La oferta cambió; vuelve a seleccionarla.",412);
                        op.Offer=offer.GetRawText();next="Reservar";
                    }
                    break;
                case "Reservar": {
                    var offer=JsonSerializer.Deserialize<JsonElement>(op.Offer!);var request=Input(op);ValidateMoney(request,offer.GetProperty("precio_venta").GetDecimal());
                    receipt=await PropertyReceipt(op,"reservar",ct)??await channel.Send("propiedades",HttpMethod.Post,$"internal/catalogo/v2/operaciones/{op.Id:D}/reservar",
                        new {operacion_id=op.Id,command_id=OperationProtocol.Command(op.Id,"reservar"),expected_version=offer.GetProperty("version").GetString(),
                            precio_esperado=request.MontoTotal,moneda_esperada=offer.GetProperty("moneda").GetString()},ct);
                    op.Reservation=((JsonElement)receipt).GetRawText();next="PrepararVenta";break;
                }
                case "PrepararVenta":
                    await PrepareSale(op,ct);next="PrepararFinanzas";break;
                case "PrepararFinanzas": {
                    await KeepReservation(op,ct);
                    var sale=await Api.Find(db.Ventas,op.VentaId!.Value,ct);
                    receipt=await FinanceReceipt(op,"Preparada",ct)??await channel.Send("finanzas",HttpMethod.Post,$"internal/finanzas/v1/operaciones/{op.Id:D}/preparar",
                        new{command_id=OperationProtocol.Command(op.Id,"preparar-finanzas"),venta=ComercialService.Snapshot(sale)},ct);
                    next="AutorizarConfirmacion";break;
                }
                case "AutorizarConfirmacion": {
                    var grant=await channel.Check(op.Id,"read",ct);
                    if(grant.State!="Confirmando") {
                        await KeepReservation(op,ct);
                        grant=await channel.Check(op.Id,"pivot",ct);
                    }
                    receipt=grant;next="ConfirmarPropiedad";break;
                }
                case "ConfirmarPropiedad":
                    receipt=await ReservationChange(op,"confirmar",ct);next="ActivarFinanzas";break;
                case "ActivarFinanzas":
                    receipt=await FinanceReceipt(op,"Activa",ct)??await channel.Send("finanzas",HttpMethod.Post,$"internal/finanzas/v1/operaciones/{op.Id:D}/activar",new{command_id=OperationProtocol.Command(op.Id,"activar-finanzas")},ct);
                    next="Completar";break;
                case "Completar":
                    next="Terminada";terminal=true;break;
                case "Abortar":
                    receipt=await Decision(op,"Abortada","abort",ct);next="AbortarFinanzas";break;
                case "AbortarFinanzas":
                    if(await FinanceReceipt(op,"Abortada",ct) is null)
                        await channel.Send("finanzas",HttpMethod.Post,$"internal/finanzas/v1/operaciones/{op.Id:D}/abortar-preparacion",new{},ct);
                    receipt=new{estado="Abortada"};next="LiberarReserva";break;
                case "LiberarReserva":
                    try{receipt=await ReservationChange(op,"liberar",ct);}catch(ProtocolException e)when(e.Status==404){ }
                    next="Compensada";terminal=true;break;
                case "CerrarFinanzas":
                    receipt=await ReadReceipt("finanzas",$"internal/finanzas/v1/operaciones/{op.Id:D}/recibo-cierre",ct)??
                        await channel.Send("finanzas",HttpMethod.Post,$"internal/finanzas/v1/operaciones/{op.Id:D}/cerrar",new{},ct);
                    next="RevertirPropiedad";break;
                case "AutorizarAnulacion":
                    receipt=await Decision(op,"Anulando","cancel-pivot",ct);next="CerrarFinanzas";break;
                case "RevertirPropiedad": {
                    var reserva=JsonSerializer.Deserialize<JsonElement>(op.Reservation!);
                    receipt=await PropertyReceipt(op,"revertir",ct)??await channel.Send("propiedades",HttpMethod.Post,$"internal/catalogo/v2/operaciones/{op.Id:D}/revertir-confirmacion",
                        new{command_id=OperationProtocol.Command(op.Id,"revertir"),fencing_token=reserva.GetProperty("fencing_token").GetInt64()},ct);
                    next="CompletarAnulacion";break;
                }
                case "CompletarAnulacion":next="Terminada";terminal=true;break;
                default:throw new BusinessException("Paso desconocido.",409);
            }
        } catch(Exception e)when(e is ProtocolException or HttpRequestException or TaskCanceledException or BusinessException or JsonException) {
            var status=e switch{ProtocolException p=>p.Status,BusinessException b=>b.Status,_=>503};
            error=status is 401 or 403?"REAUTHORIZATION_REQUIRED":status is 409 or 412 or 422?"OPERATION_CONFLICT":"RESULTADO_INCIERTO";
            if(status is 409 or 412 or 422 && op.Kind=="venta" && new[]{"Oferta","Reservar","PrepararVenta","PrepararFinanzas","AutorizarConfirmacion"}.Contains(op.Step))next="Abortar";
            else if(status==409 && op.Step=="CerrarFinanzas"){next="Rechazada";terminal=true;}
            else if(status is 409 or 412 or 422 && !new[]{"Abortar","AbortarFinanzas","LiberarReserva"}.Contains(op.Step))op.State="RequiereRevision";
        }
        observer?.Boundary(op.Id,op.Step,true);
        await FinishStep(op,lease,next,receipt,error,terminal,ct);return true;
    }
    private async Task<OperationAuthorization> Decision(SaleOperation op,string state,string action,CancellationToken ct) {
        var grant=await channel.Check(op.Id,"read",ct);
        return grant.State==state?grant:await channel.Check(op.Id,action,ct);
    }
    private async Task<JsonElement?> ReadReceipt(string authority,string route,CancellationToken ct) {
        try{return await channel.Send(authority,HttpMethod.Get,route,null,ct);}
        catch(ProtocolException e)when(e.Status==404){return null;}
    }
    private Task<JsonElement?> PropertyReceipt(SaleOperation op,string step,CancellationToken ct)=>
        ReadReceipt("propiedades",$"internal/catalogo/v2/operaciones/{op.Id:D}/recibos/{step}",ct);
    private async Task<JsonElement?> FinanceReceipt(SaleOperation op,string state,CancellationToken ct) {
        var result=await ReadReceipt("finanzas",$"internal/finanzas/v1/operaciones/{op.Id:D}",ct);
        if(result is not {} row || row.GetProperty("state").GetString()!=state)return null;
        Api.Require(row.GetProperty("id").GetGuid()==op.Id && row.GetProperty("propiedad_id").GetInt32()==op.PropiedadId &&
            (state=="Abortada" || row.GetProperty("venta_id").GetInt32()==op.VentaId),"Recibo financiero incompatible.",409);
        return row;
    }
    private CoordinatedSale Input(SaleOperation op)=>JsonSerializer.Deserialize<CoordinatedSale>(op.Payload,OperationProtocol.Json)!;
    private static void ValidateMoney(CoordinatedSale r,decimal price) {
        Api.Require(r.MontoTotal==price&&price>0&&r.MontoTotal==Api.Money(r.MontoTotal),"El precio cambió; revisa la oferta.",409);
        Api.Require(r.Descuento>=0&&r.Descuento<price&&r.Descuento==Api.Money(r.Descuento)&&r.CuotaInicial==Api.Money(r.CuotaInicial),"Importes inválidos.");
        if(r.TipoVenta=="CONTADO")Api.Require((r.MontoLiquido??price-r.Descuento)==price-r.Descuento&&r.CuotaInicial==0&&r.NumeroCuotas==0,"Contado inconsistente.");
        else Api.Require(r.Descuento==0&&r.CuotaInicial>=0&&r.CuotaInicial<price&&r.NumeroCuotas is >=1 and <=600&&r.FechaInicioPago.HasValue&&r.TasaInteres>=0&&r.TasaMora is >=0 and <=100,"Crédito inconsistente.");
    }
    private async Task PrepareSale(SaleOperation op,CancellationToken ct) {
        var r=Input(op);var reservation=JsonSerializer.Deserialize<JsonElement>(op.Reservation!);
        var price=reservation.GetProperty("precio_venta").GetDecimal();ValidateMoney(r,price);
        var propertySnapshot=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/documento",null,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        // Lease fencing also covers SQL-only steps. A stale process cannot create a sale.
        var current=await db.Set<SaleOperation>().AsNoTracking().SingleAsync(x=>x.Id==op.Id,ct);
        Api.Require(current.LeaseOwner==op.LeaseOwner&&current.LeaseUntil>DateTime.UtcNow,"Lease vencido.",409);
        var old=await db.Ventas.SingleOrDefaultAsync(x=>x.SolicitudId==op.Id,ct);
        if(old is not null){op.VentaId=old.Id;return;}
        var client=await Api.Find(db.Clientes,r.ClienteId,ct);var adviser=await Api.Find(db.Asesores,r.AsesorId,ct);
        Api.Require(client.Estado&&adviser.Estado,"Cliente o asesor inactivo.",409);
        var offer=op.Offer is null?default:JsonSerializer.Deserialize<JsonElement>(op.Offer);
        var venta=new Venta{SolicitudId=op.Id,SolicitudHash=op.PayloadHash,ClienteId=r.ClienteId,AsesorId=r.AsesorId,
            PropiedadId=r.PropiedadId,PropiedadActivaId=r.PropiedadId,CodigoPropiedad=offer.ValueKind==JsonValueKind.Object?offer.GetProperty("codigo").GetString()!:"",
            Moneda=reservation.GetProperty("moneda").GetString()!,Fecha=r.Fecha,TipoVenta=r.TipoVenta,MontoTotal=price,Descuento=r.Descuento,
            MontoLiquido=price-r.Descuento,CuotaInicial=r.CuotaInicial,SaldoCredito=r.TipoVenta=="CREDITO"?price-r.CuotaInicial:0,
            NumeroCuotas=r.NumeroCuotas,TasaInteres=r.TasaInteres,TasaMora=r.TasaMora,FechaInicioPago=r.FechaInicioPago,
            MontoComision=Api.Money(price*adviser.PorcentajeComision/100),Estado="Preparada"};
        db.Ventas.Add(venta);await db.SaveChangesAsync(ct);op.VentaId=venta.Id;
        venta.DocumentSnapshot=JsonSerializer.Serialize(new{cliente=client,asesor=adviser,propiedad=propertySnapshot},OperationProtocol.Json);
        db.Contratos.Add(new Contrato{NotaVentaId=venta.Id,CodigoContrato=$"CONT-{DateTime.UtcNow.Year}-{venta.Id:D4}",FechaEmision=r.Fecha});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task KeepReservation(SaleOperation op,CancellationToken ct) {
        var old=JsonSerializer.Deserialize<JsonElement>(op.Reservation!);
        var current=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/reserva",null,ct);
        Api.Require(current.GetProperty("estado_reserva").GetString()=="Retenida"&&current.GetProperty("fencing_token").GetInt64()==old.GetProperty("fencing_token").GetInt64(),"La reserva propia ya no está retenida.",409);
        op.Reservation=current.GetRawText();
        if(current.GetProperty("expires_at_utc").GetDateTimeOffset()<=DateTimeOffset.UtcNow.AddMinutes(2)) {
            // One deterministic renewal per pre-confirmation step. Retries cannot extend the hold forever.
            current=await ReservationChange(op,"renovar",ct,"renovar:"+op.Step);
            op.Reservation=current.GetRawText();
        }
        Api.Require(current.GetProperty("expires_at_utc").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddSeconds(15),"No hay tiempo suficiente para confirmar la reserva.",409);
    }
    private async Task<JsonElement> ReservationChange(SaleOperation op,string action,CancellationToken ct,string? commandStep=null) {
        if(await PropertyReceipt(op,commandStep??action,ct) is {} receipt)return receipt;
        JsonElement reservation;
        if(op.Reservation is null || action=="liberar") {
            reservation=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/reserva",null,ct);
            op.Reservation=reservation.GetRawText();
        } else reservation=JsonSerializer.Deserialize<JsonElement>(op.Reservation);
        if(action=="liberar"&&reservation.GetProperty("estado_reserva").GetString() is "Liberada" or "Vencida")return reservation;
        try {
            return await channel.Send("propiedades",HttpMethod.Post,$"internal/catalogo/v2/operaciones/{op.Id:D}/{action}",
                new{operacion_id=op.Id,command_id=OperationProtocol.Command(op.Id,commandStep??action),fencing_token=reservation.GetProperty("fencing_token").GetInt64(),
                    price_version=reservation.GetProperty("price_version").GetString()},ct);
        } catch(ProtocolException e) when(action=="liberar"&&e.Status==409) {
            // The hold can expire between the read and the release command.
            var current=await channel.Send("propiedades",HttpMethod.Get,$"internal/catalogo/v2/operaciones/{op.Id:D}/reserva",null,ct);
            if(current.GetProperty("estado_reserva").GetString() is "Liberada" or "Vencida")return current;
            throw;
        }
    }
    private async Task FinishStep(SaleOperation op,Guid lease,string next,object? receipt,string? error,bool terminal,CancellationToken ct) {
        var offer=op.Offer;var reservation=op.Reservation;var saleId=op.VentaId;var requestedState=op.State;var step=op.Step;
        db.ChangeTracker.Clear();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        var row=await db.Set<SaleOperation>().SingleAsync(x=>x.Id==op.Id,ct);
        if(row.LeaseOwner!=lease||row.LeaseUntil<=DateTime.UtcNow)return;
        row.Offer=offer;row.Reservation=reservation;row.VentaId=saleId;row.Attempts++;row.ErrorCode=error;
        row.LeaseOwner=null;row.LeaseUntil=null;row.UpdatedAt=DateTime.UtcNow;row.Stamp=Guid.NewGuid();
        row.NextAttempt=DateTime.UtcNow.AddSeconds(error is null?0:Math.Min(300,Math.Pow(2,Math.Min(row.Attempts,8))));
        if(next!=step) {
            var id=OperationProtocol.Command(row.Id,"receipt:"+step);
            if(!await db.Set<OperationStep>().AnyAsync(x=>x.Id==id,ct))db.Add(new OperationStep{Id=id,OperationId=row.Id,Step=step,Result=receipt is null?"{}":JsonSerializer.Serialize(receipt,OperationProtocol.Json)});
            row.Step=next;
        }
        row.State=terminal?(next=="Compensada"?"Compensada":next=="Rechazada"?"Rechazada":"Completada"):requestedState;
        if(!terminal&&row.Attempts>=64)row.State="RequiereRevision";
        if(terminal&&row.VentaId.HasValue) {
            var sale=await Api.Find(db.Ventas,row.VentaId.Value,ct);
            if(row.Kind=="venta"&&row.State=="Completada")sale.Estado="Activa";
            else if(row.Kind=="anulacion"&&row.State=="Rechazada")sale.Estado="Activa";
            else {
                sale.Estado="Anulada";sale.PropiedadActivaId=null;
                var contract=await db.Contratos.SingleAsync(x=>x.NotaVentaId==sale.Id,ct);contract.Estado="Anulado";
                var delivery=await db.Entregas.SingleOrDefaultAsync(x=>x.ContratoId==contract.Id,ct);if(delivery is not null)delivery.Estado="Anulada";
            }
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
public sealed class SaleOperationWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<SaleOperationWorker> logger):BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stop) {
        if(!config.GetValue<bool>("Operations:WorkerEnabled"))return;
        while(!stop.IsCancellationRequested) {
            try{await using var scope=scopes.CreateAsyncScope();if(await scope.ServiceProvider.GetRequiredService<SaleCoordinator>().Run(stop))continue;}
            catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Operation worker paused: {FailureType}",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(1),stop);
        }
    }
}
