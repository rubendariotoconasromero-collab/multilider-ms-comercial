using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.DTOs;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Services;
namespace MSMultilider.Comercial.Controllers;

[ApiController,Route("api/contratos"),Authorize(Policy="acceso_contratos")]
public sealed class ContratosController(AppDbContext db,ComercialService service,DocumentStore documents):ControllerBase {
    [HttpGet] public async Task<IActionResult> List(int page=1,[FromQuery(Name="per_page")]int perPage=20,CancellationToken ct=default)=>
        Ok(await Api.Paginate(db.Contratos.Where(x=>x.NotaVenta.Estado=="Activa"||x.NotaVenta.Estado=="Anulada")
            .Include(x=>x.NotaVenta).ThenInclude(x=>x.Cliente),page,perPage,ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id,CancellationToken ct)=>Ok(await Api.Find(db.Contratos,id,ct));
    [HttpPost("{id:int}/firmar")] public async Task<IActionResult> Sign(int id,FirmarContratoRequest r,CancellationToken ct)=>Ok(await service.Firmar(id,r,ct));
    [HttpPost("{id:int}/documento"),RequestSizeLimit(11*1024*1024)]
    public async Task<IActionResult> Upload(int id,IFormFile archivo,CancellationToken ct) {
        var stored=await documents.Save(archivo,ct);
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        await Multilider.Operations.OperationProtocol.Lock(db,"commercial:operations",ct);
        var row=await db.Contratos.Include(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Contrato no encontrado.",404);
        Api.Require(row.Estado!="Anulado"&&row.NotaVenta.Estado=="Activa","Venta o contrato no operativos.",409);
        row.DocumentoId=stored;db.Add(new MSMultilider.Comercial.Models.DocumentRevision{Kind="contrato",ResourceId=id,StorageId=stored,Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await documents.Read(stored,ct)))});
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Ok(new{message="Documento guardado."});
    }
    [HttpGet("{id:int}/descargar")] public async Task<IActionResult> Download(int id,CancellationToken ct) {
        var row=await Api.Find(db.Contratos,id,ct);return File(await documents.Read(row.DocumentoId,ct),"application/pdf",row.CodigoContrato+".pdf");
    }
    [HttpGet("{id:int}/generar-pdf")] public async Task<IActionResult> Pdf(int id,CancellationToken ct) {
        var row=await db.Contratos.Include(x=>x.NotaVenta).ThenInclude(x=>x.Cliente).SingleOrDefaultAsync(x=>x.Id==id,ct)
            ?? throw new BusinessException("Contrato no encontrado.",404);
        return File(SimplePdf.Create(["RESUMEN DE CONTRATO - BORRADOR",row.CodigoContrato,
            "No sustituye el contrato legal firmado.","Cliente: "+row.NotaVenta.Cliente.NombreCompleto,
            "Propiedad: "+row.NotaVenta.CodigoPropiedad,
            $"Importe: {row.NotaVenta.MontoLiquido:F2} {row.NotaVenta.Moneda}",
            "Estado: "+row.Estado]),"application/pdf",row.CodigoContrato+"-borrador.pdf");
    }
}
[ApiController,Route("api/entregas"),Authorize(Policy="acceso_entregas")]
public sealed class EntregasController(AppDbContext db,DocumentStore documents):ControllerBase {
    [HttpGet] public async Task<IActionResult> List(int page=1,[FromQuery(Name="per_page")]int perPage=20,CancellationToken ct=default)=>
        Ok(await Api.Paginate(db.Entregas.Include(x=>x.Contrato).ThenInclude(x=>x.NotaVenta).ThenInclude(x=>x.Cliente),page,perPage,ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id,CancellationToken ct)=>Ok(await Api.Find(db.Entregas,id,ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,EntregaRequest r,CancellationToken ct) {
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        await Multilider.Operations.OperationProtocol.Lock(db,"commercial:operations",ct);
        var row=await db.Entregas.Include(x=>x.Contrato).ThenInclude(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)
            ?? throw new BusinessException("Entrega no encontrada.",404);
        Api.Require(row.Contrato.Estado=="Firmado","Se requiere contrato firmado.",409);
        Api.Require(row.Contrato.NotaVenta.Estado=="Activa","La venta no está operativa.",409);
        Api.Require(row.Estado!="Entregado" && row.Estado!="Anulada","La entrega ya está cerrada.",409);
        Api.Require(r.Estado is "Pendiente" or "Entregado" or "Diferido","Estado inválido.");
        Api.Require(r.FechaProgramada>=row.Contrato.FechaFirma,"Fecha programada inválida.");
        if(r.Estado=="Entregado") {
            Api.Require(r.FechaEntrega.HasValue && r.FechaEntrega>=row.Contrato.FechaFirma &&
                !string.IsNullOrWhiteSpace(r.EntregadoPor) && !string.IsNullOrWhiteSpace(r.RecibidoPor),
                "Indica fecha de entrega, responsable y receptor.");
        } else Api.Require(r.FechaEntrega is null,"Solo una entrega completada puede tener fecha de entrega.");
        row.Estado=r.Estado;row.FechaProgramada=r.FechaProgramada;row.FechaEntrega=r.FechaEntrega;
        row.CondicionInmueble=r.CondicionInmueble;row.ItemsEntregados=r.ItemsEntregados;row.Observaciones=r.Observaciones;
        row.EntregadoPor=r.EntregadoPor;row.RecibidoPor=r.RecibidoPor;
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Ok(row);
    }
    [HttpPost("{id:int}/acta"),RequestSizeLimit(11*1024*1024)]
    public async Task<IActionResult> Upload(int id,IFormFile archivo,CancellationToken ct) {
        var stored=await documents.Save(archivo,ct);
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        await Multilider.Operations.OperationProtocol.Lock(db,"commercial:operations",ct);
        var row=await db.Entregas.Include(x=>x.Contrato).ThenInclude(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Entrega no encontrada.",404);
        Api.Require(row.Estado!="Anulada"&&row.Contrato.Estado=="Firmado"&&row.Contrato.NotaVenta.Estado=="Activa","Venta, contrato o entrega no operativos.",409);
        row.ActaId=stored;db.Add(new MSMultilider.Comercial.Models.DocumentRevision{Kind="acta",ResourceId=id,StorageId=stored,Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await documents.Read(stored,ct)))});
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Ok(new{message="Acta guardada."});
    }
    [HttpGet("{id:int}/descargar-acta")] public async Task<IActionResult> Download(int id,CancellationToken ct) {
        var row=await Api.Find(db.Entregas,id,ct);return File(await documents.Read(row.ActaId,ct),"application/pdf",$"acta-{id}.pdf");
    }
}

