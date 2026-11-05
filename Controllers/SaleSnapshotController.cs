using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
using MSMultilider.Comercial.Services;
namespace MSMultilider.Comercial.Controllers;
[ApiController]
public sealed class SaleSnapshotController(AppDbContext db,OperationChannel channel,IConfiguration config):ControllerBase {
    [HttpGet("internal/comercial/v1/operaciones/{op:guid}/snapshot"),AllowAnonymous]
    public async Task<IActionResult> Internal(Guid op,CancellationToken ct) {
        await OperationProtocol.Caller(HttpContext,config,"finanzas");var grant=await channel.Check(op,"read",ct);
        var operation=await db.Set<SaleOperation>().AsNoTracking().SingleAsync(x=>x.Id==op,ct);
        Api.Require(operation.PayloadHash==grant.PayloadHash&&operation.PropiedadId==grant.ResourceId,"Snapshot incompatible.",409);
        var sale=await Api.Find(db.Ventas,operation.VentaId!.Value,ct);
        return Ok(new{venta=ComercialService.Snapshot(sale),documento=JsonSerializer.Deserialize<JsonElement>(sale.DocumentSnapshot)});
    }
    [HttpGet("api/contratos/{id:int}/datos-documento"),Authorize(Policy="acceso_contratos")]
    public async Task<IActionResult> Document(int id,CancellationToken ct) {
        var contract=await db.Contratos.Include(x=>x.NotaVenta).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Contrato no encontrado.",404);
        Api.Require(contract.NotaVenta.Estado is "Activa" or "Anulada","Venta aún no completada.",409);
        Api.Require(contract.NotaVenta.DocumentSnapshot!="","Falta snapshot histórico; requiere migración.",409);
        return Ok(new{contrato=contract,venta=contract.NotaVenta,snapshot=JsonSerializer.Deserialize<JsonElement>(contract.NotaVenta.DocumentSnapshot)});
    }
}
