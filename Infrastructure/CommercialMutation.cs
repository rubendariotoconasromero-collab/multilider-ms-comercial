using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Infrastructure;
public sealed class CommercialMutation(RequestDelegate next) {
    public async Task Invoke(HttpContext context,AppDbContext db) {
        if(context.User.Identity?.IsAuthenticated!=true||!(context.Request.Path.StartsWithSegments("/api/contratos")||context.Request.Path.StartsWithSegments("/api/entregas"))||HttpMethods.IsGet(context.Request.Method)||HttpMethods.IsHead(context.Request.Method)||HttpMethods.IsOptions(context.Request.Method)) {
            await next(context);return;
        }
        JsonElement parsed;
        object files=Array.Empty<object>();
        if(context.Request.HasFormContentType) {
            var form=await context.Request.ReadFormAsync(context.RequestAborted);
            parsed=JsonSerializer.SerializeToElement(form.ToDictionary(x=>x.Key,x=>x.Value.ToString()));
            var fingerprints=new List<object>();
            foreach(var file in form.Files.OrderBy(x=>x.Name,StringComparer.Ordinal)) {
                Api.Require(file.Length<=10*1024*1024,"Archivo demasiado grande.",413);
                await using var input=file.OpenReadStream();
                fingerprints.Add(new{name=file.Name,length=file.Length,sha256=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,context.RequestAborted))});
            }
            files=fingerprints;
        } else {
            context.Request.EnableBuffering();
            using var reader=new StreamReader(context.Request.Body,leaveOpen:true);var body=await reader.ReadToEndAsync(context.RequestAborted);context.Request.Body.Position=0;
            parsed=string.IsNullOrWhiteSpace(body)?JsonSerializer.SerializeToElement(new{}):JsonSerializer.Deserialize<JsonElement>(body);
        }
        Guid? command=null;var raw=context.Request.Headers["Idempotency-Key"].ToString();
        if(raw==""&&parsed.TryGetProperty("solicitud_id",out var id))raw=id.ToString();
        if(raw!="") {
            if(!Guid.TryParse(raw,out var key)||key==Guid.Empty)throw new BusinessException("Clave idempotente inválida.");
            command=key;
        }
        var hash=OperationProtocol.Hash(new{method=context.Request.Method,path=context.Request.Path.Value,body=parsed,files});
        var actor=int.Parse(context.User.FindFirstValue("sub")!);
        await using var tx=await db.Database.BeginTransactionAsync(context.RequestAborted);await OperationProtocol.Lock(db,"commercial:operations",context.RequestAborted);
        if(command.HasValue&&await db.Set<CommercialCommand>().FindAsync([command.Value],context.RequestAborted) is {} receipt) {
            Api.Require(receipt.ActorId==actor,"Comando de otro actor.",403);Api.Require(receipt.Hash==hash,"UUID utilizado con otros datos.",409);
            context.Response.StatusCode=receipt.Status;context.Response.ContentType="application/json";
            context.Response.Headers["Idempotency-Replayed"]="true";await tx.CommitAsync(context.RequestAborted);await context.Response.WriteAsync(receipt.Result,context.RequestAborted);return;
        }
        var output=context.Response.Body;await using var buffer=new MemoryStream();context.Response.Body=buffer;
        try {
            await next(context);
            if(context.Response.StatusCode<400) {
                if(command.HasValue) {
                    buffer.Position=0;var result=await new StreamReader(buffer,leaveOpen:true).ReadToEndAsync(context.RequestAborted);
                    db.Add(new CommercialCommand{Id=command.Value,OperationId=parsed.TryGetProperty("operacion_id",out var operation)&&operation.TryGetGuid(out var op)?op:command.Value,
                        ActorId=actor,Hash=hash,Status=context.Response.StatusCode,Result=result});
                    await db.SaveChangesAsync(context.RequestAborted);
                }
                await tx.CommitAsync(context.RequestAborted);
            } else await tx.RollbackAsync(context.RequestAborted);
            context.Response.Body=output;buffer.Position=0;await buffer.CopyToAsync(output,context.RequestAborted);
        } finally {context.Response.Body=output;}
    }
}
