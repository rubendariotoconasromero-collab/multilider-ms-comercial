using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Services;
public sealed class ProfileCoordinator(AppDbContext db,OperationChannel channel) {
    public async Task<ProfileOperation> Begin(string kind,int? id,JsonElement input,ClaimsPrincipal actor,string bearer,string? password,CancellationToken ct) {
        Api.Require(input.TryGetProperty("operacion_id",out var operation)&&operation.TryGetGuid(out var op)&&op!=Guid.Empty,"Falta operacion_id.");
        var guid=input.GetProperty("operacion_id").GetGuid();
        var allowed=new HashSet<string>(["ci","lugar_expedicion","nombre_completo","correo","telefono","direccion","estado","porcentaje_comision","foto_id","foto_mime"],StringComparer.Ordinal);
        var profile=input.EnumerateObject().Where(x=>allowed.Contains(x.Name)).ToDictionary(x=>x.Name,x=>x.Value.Clone());
        var data=JsonSerializer.SerializeToElement(profile,OperationProtocol.Json);
        var hash=OperationProtocol.Hash(new{kind,id,profile=data});
        ProfileOperation result;
        await using(var tx=await db.Database.BeginTransactionAsync(ct)) {
            await OperationProtocol.Lock(db,"commercial:operations",ct);
            result=(await db.Set<ProfileOperation>().FindAsync([guid],ct))!;
            if(result is not null) {
                Api.Require(result.ActorIssuer==actor.FindFirstValue("iss")&&result.ActorSubject==actor.FindFirstValue("sub")&&result.PayloadHash==hash,"Operación de perfil incompatible.",409);
                if(result.State=="RequiereRevision") {
                    result.State="Pendiente";result.Attempts=0;result.NextAttempt=DateTime.UtcNow;result.ErrorCode=null;
                    await db.SaveChangesAsync(ct);
                }
            }
            else {
                var name=data.TryGetProperty("nombre_completo",out var n)?n.GetString():null;
                var email=data.TryGetProperty("correo",out var e)?e.GetString():null;
                Api.Require(!string.IsNullOrWhiteSpace(name)&&name.Length<=255&&System.Net.Mail.MailAddress.TryCreate(email,out _),"Nombre y correo requeridos.");
                int resource;
                if(kind=="cliente") {
                    var ci=data.TryGetProperty("ci",out var c)?c.GetString():null;
                    Api.Require(!string.IsNullOrWhiteSpace(ci)&&ci.Length<=50,"CI requerido.");
                    var row=id.HasValue?await Api.Find(db.Clientes,id.Value,ct):new Cliente();
                    Api.Require(row.UserId is null||row.IdentityIssuer==actor.FindFirstValue("iss"),"El vínculo histórico requiere conciliación explícita.",409);
                    if(!id.HasValue)db.Clientes.Add(row);
                    row.Ci=ci!;row.NombreCompleto=name!;row.Correo=email;row.Estado=false;
                    row.LugarExpedicion=data.TryGetProperty("lugar_expedicion",out var l)?l.GetString():null;
                    row.Telefono=data.TryGetProperty("telefono",out var t)?t.GetString():null;row.Direccion=data.TryGetProperty("direccion",out var d)?d.GetString():null;
                    await db.SaveChangesAsync(ct);resource=row.Id;
                } else {
                    var row=id.HasValue?await Api.Find(db.Asesores,id.Value,ct):new Asesor();
                    Api.Require(row.UserId is null||row.IdentityIssuer==actor.FindFirstValue("iss"),"El vínculo histórico requiere conciliación explícita.",409);
                    if(!id.HasValue)db.Asesores.Add(row);
                    row.NombreCompleto=name!;row.Correo=email;row.Estado=false;
                    if(data.TryGetProperty("foto_id",out var photo)){row.PhotoId=photo.GetString();row.PhotoMime=data.GetProperty("foto_mime").GetString();}
                    row.Telefono=data.TryGetProperty("telefono",out var t)?t.GetString():null;row.Direccion=data.TryGetProperty("direccion",out var d)?d.GetString():null;
                    if(data.TryGetProperty("porcentaje_comision",out var pct)) {
                        Api.Require(decimal.TryParse(pct.ToString(),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var percent),"Comisión inválida.");
                        row.PorcentajeComision=percent;
                    }
                    Api.Require(row.PorcentajeComision is >=0 and <=100&&row.PorcentajeComision==Api.Money(row.PorcentajeComision),"Comisión inválida.");
                    await db.SaveChangesAsync(ct);resource=row.Id;
                }
                Api.Require(!await db.Set<ProfileOperation>().AnyAsync(x=>x.Kind==kind&&x.ResourceId==resource&&x.State!="Completada",ct),"Otro cambio del perfil sigue pendiente.",409);
                result=new ProfileOperation{Id=guid,Kind=kind,ResourceId=resource,Payload=data.GetRawText(),PayloadHash=hash,
                    ActorIssuer=actor.FindFirstValue("iss")!,ActorSubject=actor.FindFirstValue("sub")!};db.Add(result);await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        if(result.State=="Completada")return result;
        try {
            await channel.Send("seguridad",HttpMethod.Post,"api/auth/operaciones",new OperationAdmission(result.Id,kind,result.ResourceId,hash),ct,bearer);
            await Continue(result,password,ct);
        }catch(Exception e)when(e is ProtocolException or HttpRequestException or TaskCanceledException) {
            await RecordFailure(result.Id,e,CancellationToken.None);
        }
        db.ChangeTracker.Clear();return (await db.Set<ProfileOperation>().FindAsync([result.Id],ct))!;
    }
    public async Task RecordFailure(Guid id,Exception error,CancellationToken ct) {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        var op=await db.Set<ProfileOperation>().FindAsync([id],ct);
        if(op is not null&&op.State!="Completada") {
            op.ErrorCode=error is ProtocolException{Status:422}?"CREDENTIAL_REQUIRED":error is ProtocolException{Status:409}?"IDENTITY_CONFLICT":error is ProtocolException{Status:401 or 403}?"REAUTHORIZATION_REQUIRED":"RESULTADO_INCIERTO";
            op.Attempts++;
            if(op.Attempts>=32)op.State="RequiereRevision";
            op.NextAttempt=DateTime.UtcNow.AddSeconds(Math.Min(300,Math.Pow(2,Math.Min(op.Attempts,8))+Random.Shared.NextDouble()));
            op.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
    public async Task Continue(ProfileOperation op,string? password,CancellationToken ct) {
        JsonElement identity;
        try{identity=await channel.Send("seguridad",HttpMethod.Get,$"internal/identity/v1/operaciones/{op.Id:D}",null,ct);}
        catch(ProtocolException e)when(e.Status==404){identity=await channel.Send("seguridad",HttpMethod.Post,$"internal/identity/v1/operaciones/{op.Id:D}/provision",new{password},ct);}
        var user=identity.GetProperty("user_id").GetInt32();
        var operationId=op.Id;db.ChangeTracker.Clear();
        await using(var bindingTx=await db.Database.BeginTransactionAsync(ct)) {
        await OperationProtocol.Lock(db,"commercial:operations",ct);
        op=(await db.Set<ProfileOperation>().FindAsync([operationId],ct))!;if(op.State=="Completada")return;
        if(op.Kind=="cliente") {
            var row=await Api.Find(db.Clientes,op.ResourceId,ct);Api.Require(row.UserId is null||row.UserId==user&&row.IdentityIssuer==identity.GetProperty("issuer").GetString(),"Vínculo existente incompatible.",409);
            row.UserId=user;row.IdentityIssuer=identity.GetProperty("issuer").GetString();row.IdentitySubject=identity.GetProperty("subject").GetString();
        } else {
            var row=await Api.Find(db.Asesores,op.ResourceId,ct);Api.Require(row.UserId is null||row.UserId==user&&row.IdentityIssuer==identity.GetProperty("issuer").GetString(),"Vínculo existente incompatible.",409);
            row.UserId=user;row.IdentityIssuer=identity.GetProperty("issuer").GetString();row.IdentitySubject=identity.GetProperty("subject").GetString();
        }
        op.State="Vinculada";op.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);await bindingTx.CommitAsync(ct);
        }
        await channel.Send("seguridad",HttpMethod.Post,$"internal/identity/v1/operaciones/{op.Id:D}/activate",new{},ct);
        db.ChangeTracker.Clear();await using var completionTx=await db.Database.BeginTransactionAsync(ct);await OperationProtocol.Lock(db,"commercial:operations",ct);
        op=(await db.Set<ProfileOperation>().FindAsync([operationId],ct))!;if(op.State=="Completada")return;
        var input=JsonSerializer.Deserialize<JsonElement>(op.Payload);
        var active=!input.TryGetProperty("estado",out var enabled)||enabled.ValueKind==JsonValueKind.True||enabled.ToString() is "1" or "true";
        if(op.Kind=="cliente")(await Api.Find(db.Clientes,op.ResourceId,ct)).Estado=active;
        else(await Api.Find(db.Asesores,op.ResourceId,ct)).Estado=active;
        op.State="Completada";op.ErrorCode=null;op.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);await completionTx.CommitAsync(ct);
    }
}
