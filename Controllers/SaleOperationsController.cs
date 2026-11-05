using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
using MSMultilider.Comercial.Services;
namespace MSMultilider.Comercial.Controllers;
[ApiController,Route("api/operaciones"),Authorize]
public sealed class SaleOperationsController(AppDbContext db,SaleCoordinator coordinator):ControllerBase {
    private string Bearer=>Request.Headers.Authorization.ToString();
    [HttpPost("ventas"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Create(CoordinatedSale request,CancellationToken ct) {
        var op=await coordinator.Begin(request,User,Bearer,ct);
        return AcceptedAtAction(nameof(Get),new{id=op.Id},op);
    }
    [HttpPost("ventas/{venta:int}/anular"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Cancel(int venta,CancelSale request,CancellationToken ct) {
        var op=await coordinator.Cancel(venta,request,User,Bearer,ct);
        return AcceptedAtAction(nameof(Get),new{id=op.Id},op);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct) {
        var op=await db.Set<SaleOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(op is not null){SaleCoordinator.Own(op,User);return Ok(op);}
        var profile=await db.Set<ProfileOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(profile is not null) {
            Api.Require(profile.ActorIssuer==User.FindFirst("iss")?.Value&&profile.ActorSubject==User.FindFirst("sub")?.Value,"Operación ajena.",403);
            return Ok(profile);
        }
        var actor=Api.UserId(User);
        var receipt=await db.Set<CommercialCommand>().AsNoTracking().FirstOrDefaultAsync(x=>x.OperationId==id&&x.ActorId==actor,ct)??throw new BusinessException("Operación no encontrada.",404);
        return Ok(new{operacion_id=id,state="Completada",resultado=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(receipt.Result)});
    }
    [HttpPost("{id:guid}/reintentar"),Authorize(Policy="acceso_ventas")]
    public async Task<IActionResult> Retry(Guid id,CancellationToken ct) {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        var op=await db.Set<SaleOperation>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new BusinessException("Operación no encontrada.",404);
        SaleCoordinator.Own(op,User);Api.Require(op.State=="RequiereRevision"&&op.LeaseOwner is null,"Operación no reanudable.",409);
        op.State="Pendiente";op.Attempts=0;op.ErrorCode=null;op.NextAttempt=DateTime.UtcNow;
        db.Add(new OperationStep{Id=Guid.NewGuid(),OperationId=id,Step="Reanudacion:"+Guid.NewGuid().ToString("N"),Result="{}"});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Accepted(op);
    }
    [HttpGet]
    public async Task<IActionResult> Pending(CancellationToken ct) {
        var issuer=User.FindFirst("iss")!.Value;var subject=User.FindFirst("sub")!.Value;
        var sales=await db.Set<SaleOperation>().AsNoTracking().Where(x=>x.ActorIssuer==issuer&&x.ActorSubject==subject&&x.State!="Completada"&&x.State!="Compensada"&&x.State!="Rechazada").OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync(ct);
        var profiles=await db.Set<ProfileOperation>().AsNoTracking().Where(x=>x.ActorIssuer==issuer&&x.ActorSubject==subject&&x.State!="Completada").OrderByDescending(x=>x.UpdatedAt).Take(100).ToListAsync(ct);
        return Ok(sales.Cast<object>().Concat(profiles).ToArray());
    }
}
