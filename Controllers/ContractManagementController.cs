using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Controllers;
[ApiController,Authorize(Policy="acceso_contratos")]
public sealed class ContractManagementController(AppDbContext db,DocumentStore documents):ControllerBase {
    [HttpPost("api/contratos/{id:int}/gestionar")]
    public async Task<IActionResult> Manage(int id,CancellationToken ct) {
        JsonElement data;string? storage=null;
        if(Request.HasFormContentType) {
            var form=await Request.ReadFormAsync(ct);data=JsonSerializer.SerializeToElement(form.ToDictionary(x=>x.Key,x=>(object)x.Value.ToString()));
            if(form.Files.GetFile("archivo") is {} file)storage=await documents.Save(file,ct);
        } else data=await JsonSerializer.DeserializeAsync<JsonElement>(Request.Body,cancellationToken:ct);
        var signed=data.TryGetProperty("firmado",out var f)&&(f.ValueKind==JsonValueKind.True||f.ToString() is "1" or "true");
        DateOnly? Date(string name)=>data.TryGetProperty(name,out var v)&&DateOnly.TryParse(v.ToString(),out var date)?date:null;
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;await OperationProtocol.Lock(db,"commercial:operations",ct);
        var contract=await db.Contratos.Include(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Contrato no encontrado.",404);
        Api.Require(contract.Estado!="Anulado"&&contract.NotaVenta.Estado=="Activa","Venta o contrato no operativos.",409);
        Api.Require(signed||!await db.Entregas.AnyAsync(x=>x.ContratoId==id&&x.Estado=="Entregado",ct),"Una entrega completada impide quitar la firma.",409);
        if(storage is not null) {
            contract.DocumentoId=storage;
            db.Add(new DocumentRevision{Kind="contrato",ResourceId=id,StorageId=storage,Sha256=Convert.ToHexString(SHA256.HashData(await documents.Read(storage,ct)))});
        }
        contract.Estado=signed?"Firmado":"Pendiente";contract.FechaFirma=signed?Date("fecha_firma")??DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"America/La_Paz")):null;
        if(signed) {
            var date=Date("fecha_programada_entrega")??contract.FechaFirma!.Value;
            var delivery=await db.Entregas.SingleOrDefaultAsync(x=>x.ContratoId==id,ct);
            if(delivery is null)db.Entregas.Add(new Entrega{ContratoId=id,FechaProgramada=date});
            else if(data.TryGetProperty("fecha_programada_entrega",out _))delivery.FechaProgramada=date;
        }
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Ok(contract);
    }
    [HttpPut("api/contratos/{id:int}/anular")]
    public async Task<IActionResult> Cancel(int id,CancellationToken ct) {
        await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;await OperationProtocol.Lock(db,"commercial:operations",ct);
        var contract=await db.Contratos.Include(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Contrato no encontrado.",404);
        Api.Require(contract.NotaVenta.Estado=="Activa","La venta está en operación.",409);
        Api.Require(!await db.Entregas.AnyAsync(x=>x.ContratoId==id&&x.Estado=="Entregado",ct),"La entrega ya se completó.",409);
        foreach(var delivery in await db.Entregas.Where(x=>x.ContratoId==id).ToListAsync(ct))delivery.Estado="Anulada";
        contract.Estado="Anulado";await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Ok(new{message="Contrato anulado."});
    }
}
