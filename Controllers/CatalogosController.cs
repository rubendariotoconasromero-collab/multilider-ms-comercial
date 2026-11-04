using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.DTOs;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Controllers;

[ApiController,Route("api/clientes"),Authorize(Policy="acceso_clientes")]
public sealed class ClientesController(AppDbContext db):ControllerBase {
    [HttpGet] public async Task<IActionResult> List(int page=1,[FromQuery(Name="per_page")] int perPage=20,CancellationToken ct=default)=>
        Ok(await Api.Paginate(db.Clientes,page,perPage,ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id,CancellationToken ct)=>Ok(await Api.Find(db.Clientes,id,ct));
    [HttpPost] public async Task<IActionResult> Create(ClienteRequest r,CancellationToken ct) {
        var row=new Cliente();Apply(row,r);db.Clientes.Add(row);await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get),new{id=row.Id},row);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,ClienteRequest r,CancellationToken ct) {
        var row=await Api.Find(db.Clientes,id,ct);Apply(row,r);await db.SaveChangesAsync(ct);return Ok(row);
    }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Disable(int id,CancellationToken ct) {
        var row=await Api.Find(db.Clientes,id,ct);row.Estado=false;await db.SaveChangesAsync(ct);return NoContent();
    }
    private static void Apply(Cliente x,ClienteRequest r) {
        x.Ci=r.Ci.Trim();x.NombreCompleto=r.NombreCompleto.Trim();x.LugarExpedicion=r.LugarExpedicion;
        x.Telefono=r.Telefono;x.Correo=r.Correo;x.Direccion=r.Direccion;x.UserId=r.UserId;x.Estado=r.Estado;
    }
}
[ApiController,Route("api/asesores"),Authorize(Policy="acceso_asesores")]
public sealed class AsesoresController(AppDbContext db):ControllerBase {
    [HttpGet] public async Task<IActionResult> List(int page=1,[FromQuery(Name="per_page")] int perPage=20,CancellationToken ct=default)=>
        Ok(await Api.Paginate(db.Asesores,page,perPage,ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id,CancellationToken ct)=>Ok(await Api.Find(db.Asesores,id,ct));
    [HttpPost] public async Task<IActionResult> Create(AsesorRequest r,CancellationToken ct) {
        var row=new Asesor();Apply(row,r);db.Asesores.Add(row);await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get),new{id=row.Id},row);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,AsesorRequest r,CancellationToken ct) {
        var row=await Api.Find(db.Asesores,id,ct);Apply(row,r);await db.SaveChangesAsync(ct);return Ok(row);
    }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Disable(int id,CancellationToken ct) {
        var row=await Api.Find(db.Asesores,id,ct);row.Estado=false;await db.SaveChangesAsync(ct);return NoContent();
    }
    private static void Apply(Asesor x,AsesorRequest r) {
        Api.Require(r.PorcentajeComision==Api.Money(r.PorcentajeComision),"La comisión admite máximo dos decimales.");
        x.NombreCompleto=r.NombreCompleto.Trim();x.PorcentajeComision=r.PorcentajeComision;
        x.Telefono=r.Telefono;x.Correo=r.Correo;x.Direccion=r.Direccion;x.UserId=r.UserId;x.Estado=r.Estado;
    }
}

