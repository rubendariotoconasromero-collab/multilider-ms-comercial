using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
namespace MSMultilider.Comercial.Controllers;
[ApiController,Route("api/comercial/opciones"),Authorize(Policy="acceso_ventas")]
public sealed class CommercialOptionsController(AppDbContext db):ControllerBase {
    [HttpGet]public async Task<IActionResult> Get(string? search,CancellationToken ct) {
        var clients=db.Clientes.Where(x=>x.Estado);
        var advisers=db.Asesores.Where(x=>x.Estado);
        if(!string.IsNullOrEmpty(search)){clients=clients.Where(x=>x.NombreCompleto.Contains(search)||x.Ci.Contains(search));advisers=advisers.Where(x=>x.NombreCompleto.Contains(search));}
        var clientOptions=await clients.OrderBy(x=>x.Id).Take(100)
            .Select(x=>new{x.Id,x.NombreCompleto,x.Ci,x.Telefono,x.Estado}).ToListAsync(ct);
        var adviserOptions=await advisers.OrderBy(x=>x.Id).Take(100)
            .Select(x=>new{x.Id,x.UserId,x.NombreCompleto,x.Correo,x.PorcentajeComision,x.Estado}).ToListAsync(ct);
        return Ok(new{clientes=clientOptions,asesores=adviserOptions});
    }
}
