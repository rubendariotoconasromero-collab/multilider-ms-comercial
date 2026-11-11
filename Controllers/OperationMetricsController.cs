using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;

namespace MSMultilider.Comercial.Controllers;

[ApiController, AllowAnonymous, Route("internal/comercial/v1/metricas-operaciones")]
public sealed class OperationMetricsController(AppDbContext db, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        await OperationProtocol.Caller(HttpContext, config, "comercial");
        var sales = await db.Set<SaleOperation>().GroupBy(x => x.State)
            .Select(g => new { state = g.Key, count = g.Count(), oldest = g.Min(x => x.CreatedAt) }).ToListAsync(ct);
        var profiles = await db.Set<ProfileOperation>().GroupBy(x => x.State)
            .Select(g => new { state = g.Key, count = g.Count(), oldest = g.Min(x => x.UpdatedAt) }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        double Age(DateTime oldest) => Math.Max(0, Math.Floor((now - oldest).TotalSeconds));
        bool Pending(string state) => state is not ("Completada" or "Rechazada" or "Compensada");
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            ventas = sales.Select(g => new { g.state, g.count, oldest_pending_seconds = Pending(g.state) ? Age(g.oldest) : (double?)null }),
            perfiles = profiles.Select(g => new { g.state, g.count, oldest_update_seconds = Pending(g.state) ? Age(g.oldest) : (double?)null }),
        });
    }
}
