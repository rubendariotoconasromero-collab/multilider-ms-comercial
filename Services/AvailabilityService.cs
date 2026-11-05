using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Services;
public sealed record PrepareAvailability(Guid OperacionId,Guid CommandId,int PropiedadId,int ClienteId,int AsesorId,string ExpectedVersion,decimal? PrecioEsperado=null,string? MonedaEsperada=null);
public sealed record ChangeAvailability(Guid CommandId,Guid ReservaId,long FencingToken);
public sealed class AvailabilityService(AppDbContext db,IAvailabilityClient remote,IHttpContextAccessor accessor) {
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    private ClaimsPrincipal Actor=>accessor.HttpContext?.User??throw new BusinessException("Sesión requerida.",401);
    private string Issuer=>Actor.FindFirstValue("iss")??throw new BusinessException("Sesión requerida.",401);
    private string Subject=>Actor.FindFirstValue("sub")??throw new BusinessException("Sesión requerida.",401);
    private void Own(AvailabilityIntent row)=>Api.Require(row.ActorIssuer==Issuer&&row.ActorSubject==Subject,"Operación ajena.",403);
    public async Task<(int Status,object Body)> Prepare(PrepareAvailability r,CancellationToken ct) {
        Api.Require(r.OperacionId!=Guid.Empty&&r.CommandId!=Guid.Empty&&!string.IsNullOrWhiteSpace(r.ExpectedVersion),"Operación, comando y versión requeridos.");
        var intent=await db.AvailabilityIntents.FindAsync([r.OperacionId],ct);
        if(intent is null) {
            var customer=await Api.Find(db.Clientes,r.ClienteId,ct);var advisor=await Api.Find(db.Asesores,r.AsesorId,ct);
            Api.Require(customer.Estado&&advisor.Estado&&r.PropiedadId>0,"Cliente, asesor y propiedad deben estar activos.");
            intent=new(){Id=r.OperacionId,ActorIssuer=Issuer,ActorSubject=Subject,ClienteId=r.ClienteId,AsesorId=r.AsesorId,PropiedadId=r.PropiedadId};
            db.AvailabilityIntents.Add(intent);
        } else {Own(intent);Api.Require(intent.PropiedadId==r.PropiedadId&&intent.ClienteId==r.ClienteId&&intent.AsesorId==r.AsesorId,"La operación identifica otros datos.",409);}
        var payload=JsonSerializer.Serialize(new{r.OperacionId,r.CommandId,r.ExpectedVersion,r.PrecioEsperado,r.MonedaEsperada},Json);
        return await Execute(intent,r.CommandId,"reservar",payload,$"internal/catalogo/v1/propiedades/{r.PropiedadId}/reservas",ct);
    }
    public async Task<(int Status,object Body)> Change(Guid operation,string action,ChangeAvailability r,CancellationToken ct) {
        Api.Require(action is "renovar" or "liberar","Transición no habilitada.",404);
        Api.Require(r.CommandId!=Guid.Empty&&r.ReservaId!=Guid.Empty&&r.FencingToken>0,"Comando y reserva requeridos.");
        var intent=await db.AvailabilityIntents.FindAsync([operation],ct)??throw new BusinessException("Operación inexistente.",404);Own(intent);
        var payload=JsonSerializer.Serialize(new{OperacionId=operation,r.CommandId,r.FencingToken},Json);
        return await Execute(intent,r.CommandId,action,payload,$"internal/catalogo/v1/reservas/{r.ReservaId:D}/{action}",ct);
    }
    private async Task<(int Status,object Body)> Execute(AvailabilityIntent intent,Guid id,string action,string payload,string path,CancellationToken ct) {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action+"|"+path+"|"+payload)));
        var command=await db.AvailabilityCommands.FindAsync([id],ct);
        if(command is not null) {Own(await db.AvailabilityIntents.SingleAsync(x=>x.Id==command.IntentId,ct));Api.Require(command.IntentId==intent.Id&&command.RequestHash==hash,"El comando identifica otros datos.",409);}
        else {
            command=new(){Id=id,IntentId=intent.Id,Intent=intent,Action=action,RequestHash=hash,Payload=payload};db.AvailabilityCommands.Add(command);
            // Durable intent and request precede HTTP; there is no transaction spanning services.
            await db.SaveChangesAsync(ct);
        }
        if(command.State=="Rechazado")return (command.HttpStatus??409,new{operacion_id=intent.Id,command_id=id,estado=command.State,code="RESERVATION_REQUEST_REJECTED"});
        try {
            if(command.State=="Aplicado")return await Current(intent,command,ct);
            var known=await remote.Send("GET",$"internal/catalogo/v1/comandos/{id:D}",null,ct);
            if(known.Status==200){await Complete(command,known,ct);return await Current(intent,command,ct);}
            if(known.Status!=404)return await Record(command,known,ct);
            if(action=="reservar") {
                var customer=await Api.Find(db.Clientes,intent.ClienteId,ct);var advisor=await Api.Find(db.Asesores,intent.AsesorId,ct);
                Api.Require(customer.Estado&&advisor.Estado,"Cliente o asesor inactivo.",409);
            }
            var result=await remote.Send("POST",path,payload,ct);
            return await Record(command,result,ct);
        } catch(Exception e) when(e is HttpRequestException or TaskCanceledException) {
            return Pending(intent.Id,id);
        }
    }
    private static (int,object) Pending(Guid operation,Guid command)=>(202,new{operacion_id=operation,command_id=command,estado="Pendiente",code="RESULTADO_INCIERTO"});
    private async Task Complete(AvailabilityCommand command,AvailabilityReply reply,CancellationToken ct) {
        var intent=await db.AvailabilityIntents.SingleAsync(x=>x.Id==command.IntentId,ct);Validate(reply.Body,intent);
        command.State="Aplicado";command.HttpStatus=200;command.Result=reply.Body.GetRawText();command.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);
    }
    private async Task<(int,object)> Record(AvailabilityCommand command,AvailabilityReply reply,CancellationToken ct) {
        if(reply.Status==200){await Complete(command,reply,ct);return(200,new{operacion_id=command.IntentId,command_id=command.Id,estado="Aplicado",reserva=reply.Body});}
        if(reply.Status>=500)return Pending(command.IntentId,command.Id);
        if(reply.Status is 401 or 403)return(reply.Status,new{code="RESERVATION_REQUEST_REJECTED",message="La sesión o permiso no permiten esta operación."});
        command.State="Rechazado";command.HttpStatus=reply.Status;command.Result=null;command.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);
        return(reply.Status is 404 or 409 or 412 or 422 or 428?reply.Status:503,new{operacion_id=command.IntentId,command_id=command.Id,estado=command.State,code="RESERVATION_REQUEST_REJECTED"});
    }
    private async Task<(int,object)> Current(AvailabilityIntent intent,AvailabilityCommand command,CancellationToken ct) {
        var reply=await remote.Send("GET",$"internal/catalogo/v1/operaciones/{intent.Id:D}/reserva",null,ct);
        if(reply.Status!=200)return Pending(intent.Id,command.Id);
        Validate(reply.Body,intent);
        return(200,new{operacion_id=intent.Id,command_id=command.Id,estado="Aplicado",reserva=reply.Body});
    }
    private static void Validate(JsonElement body,AvailabilityIntent intent) {
        if(body.ValueKind!=JsonValueKind.Object)throw new HttpRequestException("Invalid availability contract.");
        var state=body.TryGetProperty("estado_actual",out var actual)?actual:body;
        if(state.ValueKind!=JsonValueKind.Object)throw new HttpRequestException("Invalid availability contract.");
        if(!state.TryGetProperty("operacion_id",out var operation)||!operation.TryGetGuid(out var op)||op!=intent.Id
            ||!state.TryGetProperty("propiedad_id",out var property)||!property.TryGetInt32(out var id)||id!=intent.PropiedadId
            ||!state.TryGetProperty("reserva_id",out var reservation)||!reservation.TryGetGuid(out var reservationId)||reservationId==Guid.Empty
            ||!state.TryGetProperty("fencing_token",out var fence)||!fence.TryGetInt64(out var token)||token<1
            ||!state.TryGetProperty("estado_reserva",out var status)||status.ValueKind!=JsonValueKind.String||status.GetString() is not ("Retenida" or "Confirmada" or "Liberada" or "Vencida")
            ||!state.TryGetProperty("precio_venta",out var price)||!price.TryGetDecimal(out var amount)||amount<0
            ||!state.TryGetProperty("moneda",out var currency)||currency.ValueKind!=JsonValueKind.String||currency.GetString() is not ("BOB" or "USD"))throw new HttpRequestException("Invalid availability contract.");
    }
    public async Task<(int,object)> Get(Guid operation,CancellationToken ct,Guid? commandId=null) {
        var intent=await db.AvailabilityIntents.FindAsync([operation],ct)??throw new BusinessException("Operación inexistente.",404);Own(intent);
        var commands=db.AvailabilityCommands.Where(x=>x.IntentId==operation);
        if(commandId.HasValue)commands=commands.Where(x=>x.Id==commandId.Value);
        // An unresolved mutation must not be hidden by an earlier applied reservation.
        var command=await commands.OrderByDescending(x=>x.State=="Pendiente").ThenByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct)
            ??throw new BusinessException("Comando inexistente.",404);
        if(command.State=="Rechazado")return(command.HttpStatus??409,new{operacion_id=operation,command_id=command.Id,estado="Rechazado",code="RESERVATION_REQUEST_REJECTED"});
        try {
            if(command.State=="Pendiente") {var reply=await remote.Send("GET",$"internal/catalogo/v1/comandos/{command.Id:D}",null,ct);if(reply.Status==200)await Complete(command,reply,ct);else if(reply.Status is 401 or 403)return await Record(command,reply,ct);}
            return command.State=="Aplicado"?await Current(intent,command,ct):Pending(operation,command.Id);
        } catch(Exception e) when(e is HttpRequestException or TaskCanceledException){return Pending(operation,command.Id);}
    }
}
