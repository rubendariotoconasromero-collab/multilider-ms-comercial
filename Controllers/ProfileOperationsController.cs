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
public sealed class ProfileOperationsController(AppDbContext db,ProfileCoordinator coordinator,IConfiguration config,ProfilePhotos photos):ControllerBase {
    [HttpPost("api/perfiles/{kind:regex(^(clientes|asesores)$)}"),Authorize]
    [HttpPut("api/perfiles/{kind:regex(^(clientes|asesores)$)}/{id:int}"),Authorize]
    public async Task<IActionResult> Change(string kind,int? id,CancellationToken ct) {
        var type=kind=="clientes"?"cliente":"asesor";
        Api.Require(User.HasClaim("permission",kind=="clientes"?"acceso_clientes":"acceso_asesores"),"Permiso requerido.",403);
        JsonElement input;(string Id,string Mime)? photograph=null;
        if(Request.HasFormContentType) {
            var form=await Request.ReadFormAsync(ct);input=JsonSerializer.SerializeToElement(form.ToDictionary(x=>x.Key,x=>(object?)x.Value.ToString()));
            if(type=="asesor"&&form.Files.GetFile("foto") is {} file)photograph=await photos.Save(file,ct);
        } else input=await JsonSerializer.DeserializeAsync<JsonElement>(Request.Body,cancellationToken:ct);
        var values=input.EnumerateObject().Where(x=>x.Name is not ("foto_id" or "foto_mime")).ToDictionary(x=>x.Name,x=>(object?)x.Value.Clone());
        if(photograph.HasValue){values["foto_id"]=photograph.Value.Id;values["foto_mime"]=photograph.Value.Mime;}
        input=JsonSerializer.SerializeToElement(values,OperationProtocol.Json);
        var password=input.TryGetProperty("password",out var p)?p.GetString():null;
        var result=await coordinator.Begin(type,id,input,User,Request.Headers.Authorization.ToString(),password,ct);
        return result.State=="Completada"?Ok(new{operacion_id=result.Id,estado_operacion=result.State,data=type=="cliente"?(object)await Api.Find(db.Clientes,result.ResourceId,ct):await Api.Find(db.Asesores,result.ResourceId,ct)}):Accepted(result);
    }
    [HttpGet("api/asesores/{id:int}/foto"),Authorize(Policy="acceso_asesores")]
    public async Task<IActionResult> Photo(int id,CancellationToken ct) {
        var adviser=await Api.Find(db.Asesores,id,ct);
        return File(await photos.Read(adviser.PhotoId,ct),adviser.PhotoMime??"application/octet-stream");
    }
    [HttpDelete("api/perfiles/{kind:regex(^(clientes|asesores)$)}/{id:int}"),Authorize]
    public async Task<IActionResult> Disable(string kind,int id,JsonElement command,CancellationToken ct) {
        var type=kind=="clientes"?"cliente":"asesor";Api.Require(User.HasClaim("permission",kind=="clientes"?"acceso_clientes":"acceso_asesores"),"Permiso requerido.",403);
        var operation=command.GetProperty("operacion_id").GetGuid();
        if(await db.Set<ProfileOperation>().FindAsync([operation],ct) is {} old) {
            Api.Require(old.Kind==type&&old.ResourceId==id&&old.ActorIssuer==User.FindFirst("iss")?.Value&&old.ActorSubject==User.FindFirst("sub")?.Value,"Operación incompatible.",409);
            return old.State=="Completada"?Ok(new{operacion_id=old.Id,estado_operacion=old.State,estado=false}):Accepted(old);
        }
        object row=type=="cliente"?await Api.Find(db.Clientes,id,ct):await Api.Find(db.Asesores,id,ct);
        var data=JsonSerializer.SerializeToElement(row,OperationProtocol.Json).EnumerateObject().ToDictionary(x=>x.Name,x=>(object?)x.Value.Clone());
        data["estado"]=false;
        data["operacion_id"]=command.GetProperty("operacion_id").GetString();
        var result=await coordinator.Begin(type,id,JsonSerializer.SerializeToElement(data,OperationProtocol.Json),User,Request.Headers.Authorization.ToString(),null,ct);
        return result.State=="Completada"?Ok(new{operacion_id=result.Id,estado_operacion=result.State,estado=false}):Accepted(result);
    }
    [HttpGet("api/perfiles/operaciones/{id:guid}"),Authorize]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct) {
        var op=await db.Set<ProfileOperation>().FindAsync([id],ct)??throw new BusinessException("Operación no encontrada.",404);
        Api.Require(op.ActorIssuer==User.FindFirst("iss")?.Value&&op.ActorSubject==User.FindFirst("sub")?.Value,"Operación ajena.",403);
        return Ok(op);
    }
    [HttpGet("internal/comercial/v1/perfiles/{id:guid}"),AllowAnonymous]
    public async Task<IActionResult> Internal(Guid id,CancellationToken ct) {
        await OperationProtocol.Caller(HttpContext,config,"seguridad");
        var op=await db.Set<ProfileOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Perfil pendiente.",404);
        return Ok(new{op.Id,op.State,op.ResourceId,op.PayloadHash,profile=JsonSerializer.Deserialize<JsonElement>(op.Payload),
            user_id=op.Kind=="cliente"?(await Api.Find(db.Clientes,op.ResourceId,ct)).UserId:(await Api.Find(db.Asesores,op.ResourceId,ct)).UserId});
    }
}
