using MSMultilider.Comercial.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
using MSMultilider.Comercial.Services;
using Xunit;
namespace MSMultilider.Comercial.Tests;
public sealed class AvailabilityTests {
    private sealed class Remote: IAvailabilityClient {
        public Func<string,string,string?,Task<AvailabilityReply>> Run=null!;
        public Task<AvailabilityReply> Send(string method,string path,string? payload,CancellationToken ct)=>Run(method,path,payload);
    }
    private static AvailabilityReply Reply(int status,object data)=>new(status,JsonSerializer.SerializeToElement(data));
    private static async Task Seed(ApiFactory app){using var s=app.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<AppDbContext>();db.Clientes.Add(new(){NombreCompleto="Fixture",Ci="fixture"});db.Asesores.Add(new(){NombreCompleto="Fixture"});await db.SaveChangesAsync();}
    [Fact]public async Task LostReplyIsRecoveredWithSameCommandAndDurableIntentWithoutSavingBearer() {
        var remote=new Remote();using var app=new ApiFactory{Availability=remote,UseLegacyCharacterizationGate=false};using var c=app.Client(ApiFactory.AllPermissions);await Seed(app);
        var operation=Guid.NewGuid();var command=Guid.NewGuid();var postCount=0;bool committed=false;
        var state=new{reserva_id=Guid.NewGuid(),operacion_id=operation,propiedad_id=77,estado_reserva="Retenida",estado_propiedad="Reservado",fencing_token=1,precio_venta=10000.25m,moneda="BOB"};
        remote.Run=async(method,path,payload)=>{
            using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(db.Database.CurrentTransaction);Assert.Equal(1,await db.AvailabilityIntents.CountAsync());
            var saved=await db.AvailabilityCommands.SingleAsync();Assert.DoesNotContain("Bearer",saved.Payload);Assert.DoesNotContain(ApiFactory.TestKey,saved.Payload);
            if(method=="POST"){postCount++;committed=true;throw new HttpRequestException("Lost reply after commit");}
            return path.Contains("/comandos/")?(committed?Reply(200,new{resultado_comando=state,estado_actual=state}):Reply(404,new{})):Reply(200,state);
        };
        var body=new PrepareAvailability(operation,command,77,1,1,"fixture-version");
        Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/disponibilidad/operaciones",body,RelatedApi.JsonOptions)).StatusCode);
        var recovered=await(await c.GetAsync($"/api/disponibilidad/operaciones/{operation}")).Json();Assert.Equal("Retenida",recovered.GetProperty("reserva").GetProperty("estado_reserva").GetString());
        Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/disponibilidad/operaciones",body,RelatedApi.JsonOptions)).StatusCode);Assert.Equal(1,postCount);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/disponibilidad/operaciones",body with{PropiedadId=88},RelatedApi.JsonOptions)).StatusCode);
        using var s=app.Services.CreateScope();Assert.Equal(0,await s.ServiceProvider.GetRequiredService<AppDbContext>().Ventas.CountAsync());
    }
    [Fact]public async Task LostReleaseReplyRemainsPendingUntilItsOwnCommandIsRecovered() {
        var remote=new Remote();using var app=new ApiFactory{Availability=remote};using var c=app.Client(ApiFactory.AllPermissions);await Seed(app);
        var operation=Guid.NewGuid();var reserve=Guid.NewGuid();var release=Guid.NewGuid();var reservation=Guid.NewGuid();var receipts=new Dictionary<Guid,JsonElement>();var released=false;var releaseCalls=0;
        JsonElement State()=>JsonSerializer.SerializeToElement(new{reserva_id=reservation,operacion_id=operation,propiedad_id=77,estado_reserva=released?"Liberada":"Retenida",estado_propiedad=released?"Disponible":"Reservado",fencing_token=1,precio_venta=10000m,moneda="BOB"});
        remote.Run=(method,path,payload)=>{
            if(method=="GET"&&path.Contains("/comandos/")){var id=Guid.Parse(path.Split('/').Last());return Task.FromResult(receipts.TryGetValue(id,out var receipt)?Reply(200,new{resultado_comando=receipt,estado_actual=State()}):Reply(404,new{}));}
            if(method=="GET")return Task.FromResult(new AvailabilityReply(200,State()));
            var id2=JsonDocument.Parse(payload!).RootElement.GetProperty("command_id").GetGuid();
            if(path.EndsWith("/liberar")){released=true;releaseCalls++;receipts[id2]=State();throw new HttpRequestException("Lost release reply after commit");}
            receipts[id2]=State();return Task.FromResult(new AvailabilityReply(200,State()));
        };
        Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/disponibilidad/operaciones",new PrepareAvailability(operation,reserve,77,1,1,"v"),RelatedApi.JsonOptions)).StatusCode);
        var body=new ChangeAvailability(release,reservation,1);Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync($"/api/disponibilidad/operaciones/{operation}/liberar",body,RelatedApi.JsonOptions)).StatusCode);
        var recovered=await(await c.GetAsync($"/api/disponibilidad/operaciones/{operation}?command_id={release}")).Json();Assert.Equal(release,recovered.GetProperty("command_id").GetGuid());Assert.Equal("Liberada",recovered.GetProperty("reserva").GetProperty("estado_reserva").GetString());
        Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync($"/api/disponibilidad/operaciones/{operation}/liberar",body,RelatedApi.JsonOptions)).StatusCode);Assert.Equal(1,releaseCalls);
    }
    [Fact]public async Task ForeignActorAndNoPermissionCannotReadOrResumeIntent() {
        var remote=new Remote{Run=(_,_,_)=>Task.FromResult(Reply(503,new{}))};using var app=new ApiFactory{Availability=remote};using var c=app.Client(ApiFactory.AllPermissions);await Seed(app);
        var op=Guid.NewGuid();await c.PostAsJsonAsync("/api/disponibilidad/operaciones",new PrepareAvailability(op,Guid.NewGuid(),77,1,1,"v"),RelatedApi.JsonOptions);
        app.IdentitySubject=2;c.DefaultRequestHeaders.Authorization=new("Bearer",ApiFactory.Token(ApiFactory.AllPermissions,subject:2));Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync($"/api/disponibilidad/operaciones/{op}")).StatusCode);
        app.IdentitySubject=1;app.AuthoritativePermissions=["acceso_clientes"];c.DefaultRequestHeaders.Authorization=new("Bearer",ApiFactory.Token(ApiFactory.AllPermissions));Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync($"/api/disponibilidad/operaciones/{op}")).StatusCode);
    }
    [Fact]public async Task UnavailableCatalogLeavesPendingAndNeverActivatesSaleOrContract() {
        var calls=0;var remote=new Remote{Run=(_,_,_)=>{calls++;throw new HttpRequestException();}};using var app=new ApiFactory{Availability=remote,UseLegacyCharacterizationGate=false};using var c=app.Client(ApiFactory.AllPermissions);await Seed(app);
        Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/disponibilidad/operaciones",new PrepareAvailability(Guid.NewGuid(),Guid.NewGuid(),77,1,1,"v"),RelatedApi.JsonOptions)).StatusCode);
        var response=await c.PostAsJsonAsync("/api/ventas",new{solicitud_id=Guid.NewGuid(),cliente_id=1,asesor_id=1,propiedad_id=77,fecha="2026-11-04",tipo_venta="CONTADO"});Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        using var s=app.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(0,await db.Ventas.CountAsync());Assert.Equal(0,await db.Contratos.CountAsync());Assert.Equal("Pendiente",(await db.AvailabilityCommands.SingleAsync()).State);Assert.Equal(1,calls);
    }
}
