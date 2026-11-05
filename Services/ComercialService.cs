using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.DTOs;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Services;

public sealed class PropiedadesClient(HttpClient client,IHttpContextAccessor accessor):RelatedApi(client,accessor);
public sealed class FinanzasClient(HttpClient client,IHttpContextAccessor accessor):RelatedApi(client,accessor);
public sealed class ComercialService(AppDbContext db,PropiedadesClient propiedades,FinanzasClient finanzas,ISaleActivationGate activation) {
    public async Task<Venta> CrearVenta(VentaRequest r,CancellationToken ct) {
        activation.RequireReady();
        Api.Require(r.SolicitudId!=Guid.Empty,"SolicitudId debe ser un UUID para evitar duplicados.");
        Api.Require(r.Fecha!=default,"Fecha requerida.");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r))));
        var previous=await db.Ventas.SingleOrDefaultAsync(x=>x.SolicitudId==r.SolicitudId,ct);
        if(previous is not null) {
            Api.Require(previous.SolicitudHash==hash,"La solicitud ya existe con otros datos.",409);
            return previous;
        }
        var cliente=await Api.Find(db.Clientes,r.ClienteId,ct);
        var asesor=await Api.Find(db.Asesores,r.AsesorId,ct);
        Api.Require(cliente.Estado && asesor.Estado,"Cliente o asesor inactivo.");
        Api.Require(r.TipoVenta is "CONTADO" or "CREDITO","Tipo de venta inválido.");
        Api.Require(r.TasaInteres==Api.Money(r.TasaInteres) && r.TasaMora==Api.Money(r.TasaMora),"Las tasas admiten máximo dos decimales.");
        var propiedad=await propiedades.Get<PropiedadSnapshot>($"api/propiedades/{r.PropiedadId}",ct);
        Api.Require(propiedad.Id==r.PropiedadId && propiedad.Activo && propiedad.Estado=="Disponible","La propiedad no está disponible.");
        Api.Currency(propiedad.Moneda);
        Api.Require(propiedad.PrecioVenta>0 && r.Descuento<propiedad.PrecioVenta,"Precio o descuento inválido.");
        Api.Require(propiedad.PrecioVenta==Api.Money(propiedad.PrecioVenta),"El precio del catálogo debe tener máximo dos decimales.");
        Api.Require(r.Descuento==Api.Money(r.Descuento) && r.CuotaInicial==Api.Money(r.CuotaInicial),"Usa importes con máximo dos decimales.");
        var neto=Api.Money(propiedad.PrecioVenta-r.Descuento);
        if(r.TipoVenta=="CREDITO") {
            Api.Require(r.CuotaInicial<neto && r.NumeroCuotas>=1 && r.FechaInicioPago.HasValue && r.FechaInicioPago.Value>=r.Fecha,
                "El crédito requiere saldo positivo, cuotas y fecha de inicio válida.");
        } else Api.Require(r.CuotaInicial==0 && r.NumeroCuotas==0 && r.TasaInteres==0 && r.TasaMora==0 && r.FechaInicioPago is null,"No envíes condiciones de crédito para una venta al contado.");
        Api.Require(!await db.Ventas.AnyAsync(x=>x.PropiedadActivaId==r.PropiedadId,ct),"La propiedad ya tiene una venta activa.",409);
        var venta=new Venta { SolicitudId=r.SolicitudId,SolicitudHash=hash,ClienteId=r.ClienteId,AsesorId=r.AsesorId,
            PropiedadId=r.PropiedadId,PropiedadActivaId=r.PropiedadId,CodigoPropiedad=propiedad.Codigo,Moneda=propiedad.Moneda,
            Fecha=r.Fecha,TipoVenta=r.TipoVenta,MontoTotal=propiedad.PrecioVenta,Descuento=r.Descuento,MontoLiquido=neto,
            MontoComision=Api.Money(propiedad.PrecioVenta*asesor.PorcentajeComision/100),CuotaInicial=r.CuotaInicial,
            SaldoCredito=r.TipoVenta=="CREDITO"?neto-r.CuotaInicial:0,NumeroCuotas=r.NumeroCuotas,
            TasaInteres=r.TasaInteres,TasaMora=r.TasaMora,FechaInicioPago=r.FechaInicioPago };
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        db.Ventas.Add(venta);await db.SaveChangesAsync(ct);
        db.Contratos.Add(new Contrato { NotaVentaId=venta.Id,CodigoContrato=$"CONT-{r.Fecha.Year}-{venta.Id:D6}",FechaEmision=r.Fecha });
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);
        return venta;
    }
    public static VentaFinanciera Snapshot(Venta v)=>new(v.Id,v.ClienteId,v.AsesorId,v.PropiedadId,v.Fecha,v.Estado,v.TipoVenta,
        v.Moneda,v.MontoLiquido,v.CuotaInicial,v.SaldoCredito,v.MontoComision,v.NumeroCuotas,v.TasaInteres,v.TasaMora,v.FechaInicioPago);
    public async Task AnularVenta(int id,CancellationToken ct) {
        activation.RequireReady();
        var venta=await Api.Find(db.Ventas,id,ct);
        if(venta.Estado=="Anulada") return;
        Api.Require(!await db.Entregas.AnyAsync(x=>x.Contrato.NotaVentaId==id && x.Estado=="Entregado",ct),"No se puede anular una venta entregada.",409);
        // Finanzas keeps a cancellation tombstone even if the sale was not registered.
        await finanzas.Post($"api/ventas/{id}/anular",ct);
        venta.Estado="Anulada";venta.PropiedadActivaId=null;
        var contrato=await db.Contratos.SingleAsync(x=>x.NotaVentaId==id,ct);contrato.Estado="Anulado";
        var entrega=await db.Entregas.SingleOrDefaultAsync(x=>x.ContratoId==contrato.Id,ct);
        if(entrega is not null) entrega.Estado="Anulada";
        await db.SaveChangesAsync(ct);
    }
    public async Task<Contrato> Firmar(int id,FirmarContratoRequest r,CancellationToken ct) {
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        await Multilider.Operations.OperationProtocol.Lock(db,"commercial:operations",ct);
        Api.Require(r.FechaFirma!=default && r.FechaProgramadaEntrega>=r.FechaFirma,"Fechas de firma y entrega inválidas.");
        var contrato=await db.Contratos.Include(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)
            ?? throw new BusinessException("Contrato no encontrado.",404);
        Api.Require(contrato.Estado!="Anulado" && contrato.NotaVenta.Estado=="Activa","Contrato o venta anulados.",409);
        contrato.Estado="Firmado";contrato.FechaFirma=r.FechaFirma;
        var entrega=await db.Entregas.SingleOrDefaultAsync(x=>x.ContratoId==id,ct);
        if(entrega is null) db.Entregas.Add(new Entrega { ContratoId=id,FechaProgramada=r.FechaProgramadaEntrega });
        else { Api.Require(entrega.Estado!="Entregado","La entrega ya se completó.",409);entrega.FechaProgramada=r.FechaProgramadaEntrega; }
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return contrato;
    }
}

