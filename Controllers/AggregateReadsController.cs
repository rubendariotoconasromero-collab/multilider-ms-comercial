using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;

namespace MSMultilider.Comercial.Controllers;

[ApiController, Authorize, Route("api/lecturas")]
public sealed class AggregateReadsController(AppDbContext db, ProfilePhotos photos) : ControllerBase
{
    [HttpGet("mi-cliente")]
    public async Task<IActionResult> Customer(CancellationToken ct)
    {
        var c = await FindCustomer(ct);
        return Ok(new { c.Id, c.NombreCompleto, c.Ci, c.LugarExpedicion, c.Telefono, c.Correo });
    }

    [HttpGet("/internal/lecturas/v1/{dataset}/perfil/{kind}")]
    public async Task<IActionResult> FinanceProfile(string dataset, string kind, [FromServices] IConfiguration config, CancellationToken ct)
    {
        await OperationProtocol.Caller(HttpContext, config, "finanzas");
        Api.Require(string.Equals(dataset, config["Operations:Dataset"], StringComparison.Ordinal),
            "Conjunto de datos incompatible.", 403);
        return kind switch
        {
            "mi-cliente" => Ok(new { (await FindCustomer(ct)).Id }),
            "mi-asesor" => Ok(new { (await FindAdviser(ct)).Id }),
            _ => NotFound()
        };
    }

    [HttpGet("mi-asesor")]
    public async Task<IActionResult> Adviser(CancellationToken ct)
    {
        var adviser = await FindAdviser(ct);
        return Ok(new { adviser.Id, adviser.NombreCompleto });
    }

    [HttpGet("ventas")]
    public async Task<IActionResult> Sales(DateOnly? desde, DateOnly? hasta, bool propio = false,
        int page = 1, [FromQuery(Name = "per_page")] int size = 100, CancellationToken ct = default)
    {
        var query = db.Ventas.AsNoTracking().Where(x => x.Estado == "Activa" || x.Estado == "Anulada");
        if (propio)
        {
            var adviser = await FindAdviser(ct);
            query = query.Where(x => x.AsesorId == adviser.Id);
        }
        else Api.Require(User.HasClaim("permission", "acceso_historial_ventas"), "Permiso requerido.", 403);
        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        var result = await Api.Paginate(query.Include(x => x.Cliente).Include(x => x.Asesor), page, size, ct);
        var data = result.Data.Select(x => new {
            x.Id, x.Fecha, x.Estado, x.TipoVenta, x.Moneda, x.MontoTotal, x.MontoComision, x.SaldoCredito,
            x.ClienteId, x.AsesorId, x.PropiedadId,
            cliente = new { x.Cliente.Id, x.Cliente.NombreCompleto },
            asesor = new { x.Asesor.Id, x.Asesor.NombreCompleto },
            propiedad = new { id = x.PropiedadId, codigo = x.CodigoPropiedad, x.Moneda }
        });
        return Ok(new { data, result.Total, result.PageNumber, result.PerPage, result.LastPage });
    }

    [HttpGet("asesores")]
    public async Task<IActionResult> Advisers(int page = 1, [FromQuery(Name = "per_page")] int size = 100,
        CancellationToken ct = default)
    {
        Api.Require(new[] { "acceso_historial_ventas", "acceso_pagos", "acceso_egresos" }
            .Any(p => User.HasClaim("permission", p)), "Permiso requerido.", 403);
        var result = await Api.Paginate(db.Asesores.AsNoTracking().Where(x => x.Estado), page, size, ct);
        return Ok(new { data = result.Data.Select(x => new { x.Id, x.NombreCompleto, x.PorcentajeComision }),
            result.Total, result.PageNumber, result.PerPage, result.LastPage });
    }

    [HttpGet("destinatarios"), Authorize(Policy = "acceso_correo_masivo")]
    public async Task<IActionResult> Recipients(bool? activo, string? ids, string? propiedades,
        int page = 1, [FromQuery(Name = "per_page")] int size = 100, CancellationToken ct = default)
    {
        var query = db.Clientes.AsNoTracking().Where(x => x.Correo != null && x.Correo != "");
        if (activo.HasValue) query = query.Where(x => x.Estado == activo.Value);
        if (ids is not null)
        {
            var selected = ReadIds(ids);
            query = query.Where(x => selected.Contains(x.Id));
        }
        if (propiedades is not null)
        {
            var properties = ReadIds(propiedades);
            query = query.Where(x => db.Ventas.Any(v => v.ClienteId == x.Id && v.Estado == "Activa" && properties.Contains(v.PropiedadId)));
        }
        var result = await Api.Paginate(query, page, size, ct);
        return Ok(new { data = result.Data.Select(x => new { x.Id, nombre = x.NombreCompleto, x.Correo, x.Estado }),
            result.Total, result.PageNumber, result.PerPage, result.LastPage });
    }

    [HttpGet("asesores-usuarios")]
    public async Task<IActionResult> AdviserUsers(string ids, CancellationToken ct)
    {
        var allowed = new[] { "acceso_historial_ventas", "acceso_pagos", "acceso_egresos", "acceso_propiedades" };
        Api.Require(allowed.Any(p => User.HasClaim("permission", p)), "Permiso requerido.", 403);
        var selection = ReadIds(ids);
        var issuer = User.FindFirstValue("iss");
        var rows = await db.Asesores.AsNoTracking().Where(x => x.Estado && selection.Contains(x.Id) &&
            x.IdentityIssuer == issuer && x.IdentitySubject != null)
            .Select(x => new { x.Id, subject = x.IdentitySubject }).ToListAsync(ct);
        return Ok(new { data = rows });
    }

    [HttpGet("asesores-publicos"), AllowAnonymous]
    public async Task<IActionResult> PublicAdvisers(CancellationToken ct)
    {
        var advisers = await db.Asesores.AsNoTracking().Where(x => x.Estado).OrderBy(x => x.Id).Take(50).ToListAsync(ct);
        return Ok(advisers.Select(x => new { x.Id, x.NombreCompleto, x.Telefono,
            foto = x.Foto == null ? null : $"/api/lecturas/asesores-publicos/{x.Id}/foto" }));
    }

    [HttpGet("asesores-publicos/{id:int}/foto"), AllowAnonymous]
    public async Task<IActionResult> PublicPhoto(int id, CancellationToken ct)
    {
        var adviser = await db.Asesores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Estado, ct)
            ?? throw new BusinessException("Asesor no encontrado.", 404);
        return File(await photos.Read(adviser.PhotoId, ct), adviser.PhotoMime ?? "application/octet-stream");
    }

    private async Task<Asesor> FindAdviser(CancellationToken ct)
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        Api.Require(!string.IsNullOrEmpty(issuer) && !string.IsNullOrEmpty(subject), "Identidad inválida.", 401);
        var rows = await db.Asesores.AsNoTracking().Where(x => x.Estado &&
            x.IdentityIssuer == issuer && x.IdentitySubject == subject).Take(2).ToListAsync(ct);
        Api.Require(rows.Count == 1, "La sesión no tiene un vínculo de asesor vigente.", 403);
        return rows[0];
    }

    private async Task<Cliente> FindCustomer(CancellationToken ct)
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        Api.Require(!string.IsNullOrEmpty(issuer) && !string.IsNullOrEmpty(subject), "Identidad inválida.", 401);
        var rows = await db.Clientes.AsNoTracking().Where(x => x.Estado &&
            x.IdentityIssuer == issuer && x.IdentitySubject == subject).Take(2).ToListAsync(ct);
        Api.Require(rows.Count == 1, "La sesión no tiene un vínculo de cliente vigente.", 404);
        return rows[0];
    }

    private static int[] ReadIds(string value)
    {
        var ids = value.Split(',').Select(x => int.TryParse(x, out var id) ? id : 0).Distinct().ToArray();
        Api.Require(ids.Length is > 0 and <= 100 && ids.All(x => x > 0), "Identificadores inválidos.");
        return ids;
    }
}
