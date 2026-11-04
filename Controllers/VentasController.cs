using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.DTOs;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Services;
namespace MSMultilider.Comercial.Controllers;

[ApiController,Route("api/ventas"),Authorize]
public sealed class VentasController(AppDbContext db,ComercialService service):ControllerBase {
    [HttpGet,Authorize(Policy="acceso_historial_ventas")]
    public async Task<IActionResult> List([FromQuery(Name="cliente_id")]int? clienteId,[FromQuery(Name="asesor_id")]int? asesorId,
        string? estado,int page=1,[FromQuery(Name="per_page")]int perPage=20,CancellationToken ct=default) {
        var query=db.Ventas.Include(x=>x.Cliente).Include(x=>x.Asesor).AsQueryable();
        if(clienteId.HasValue) query=query.Where(x=>x.ClienteId==clienteId);
        if(asesorId.HasValue) query=query.Where(x=>x.AsesorId==asesorId);
        if(estado is not null) query=query.Where(x=>x.Estado==estado);
        return Ok(await Api.Paginate(query,page,perPage,ct));
    }
    [HttpGet("{id:int}"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Get(int id,CancellationToken ct)=>
        Ok(await db.Ventas.Include(x=>x.Cliente).Include(x=>x.Asesor).SingleOrDefaultAsync(x=>x.Id==id,ct)
            ?? throw new BusinessException("Venta no encontrada.",404));
    [HttpGet("{id:int}/financiacion"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Financing(int id,CancellationToken ct)=>Ok(ComercialService.Snapshot(await Api.Find(db.Ventas,id,ct)));
    [HttpPost,Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Create(VentaRequest r,CancellationToken ct) {
        var row=await service.CrearVenta(r,ct);return CreatedAtAction(nameof(Get),new{id=row.Id},row);
    }
    [HttpPut("{id:int}/anular"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Cancel(int id,CancellationToken ct) {
        await service.AnularVenta(id,ct);return Ok(new {message="Venta y obligaciones financieras anuladas."});
    }
}

