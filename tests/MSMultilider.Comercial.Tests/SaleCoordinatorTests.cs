using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
using MSMultilider.Comercial.Services;
using Xunit;
namespace MSMultilider.Comercial.Tests;
public sealed class SaleCoordinatorTests {
    private sealed class Crash(string target,bool after):IOperationObserver {
        public bool Fired;
        public void Boundary(Guid id,string step,bool isAfter) {if(!Fired&&step==target&&after==isAfter){Fired=true;throw new InvalidOperationException("simulated process loss");}}
    }
    private sealed class Remote(IConfiguration config):OperationChannel(config) {
        public Guid Operation;
        public string State="Admitida";
        public string ReservationState="";
        public int FinancePreparations,FinanceActivations;
        public DateTimeOffset Expires=DateTimeOffset.UtcNow.AddMinutes(15);
        public int Renewals;
        public int Confirmations;
        public bool LoseConfirmOnce;
        private bool lost;
        private readonly Dictionary<string,JsonElement> receipts=new();
        private JsonElement Result(object value)=>JsonSerializer.SerializeToElement(value,OperationProtocol.Json);
        private JsonElement Reservation()=>Result(new{reserva_id=Guid.Parse("11111111-1111-4111-8111-111111111111"),operacion_id=Operation,propiedad_id=1,fencing_token=1L,estado_reserva=ReservationState,
            estado_propiedad=ReservationState=="Confirmada"?"Vendido":"Reservado",precio_venta=1000m,moneda="BOB",price_version="v1",version="v2",expires_at_utc=Expires});
        public override Task<OperationAuthorization> Check(Guid id,string action,CancellationToken ct) {
            if(action=="pivot")State="Confirmando";if(action=="abort")State="Abortada";
            return Task.FromResult(new OperationAuthorization(id,"venta",1,"","tests","1",State,Dataset:"tests"));
        }
        public override Task<JsonElement> Send(string service,HttpMethod method,string path,object? body,CancellationToken ct,string? bearer=null) {
            if(service=="seguridad"){Operation=((OperationAdmission)body!).OperationId;return Task.FromResult(Result(new{accepted=true}));}
            if(path.EndsWith("/oferta"))return Task.FromResult(Result(new{id=1,codigo="TEST",precio_venta=1000m,moneda="BOB",version="v1",estado="Disponible",activo=true}));
            if(path.EndsWith("/documento"))return Task.FromResult(Result(new{id=1,codigo="TEST",propietarios=Array.Empty<object>()}));
            if(path.EndsWith("/reserva")) {
                if(ReservationState=="")throw new ProtocolException("not found",404);
                return Task.FromResult(Reservation());
            }
            if(method==HttpMethod.Get) {
                var key=path.Contains("/recibos/")?path.Split('/').Last():"finance";
                return receipts.TryGetValue(key,out var receipt)?Task.FromResult(receipt):throw new ProtocolException("not found",404);
            }
            if(path.EndsWith("/reservar")){ReservationState="Retenida";return Task.FromResult(receipts["reservar"]=Reservation());}
            if(path.EndsWith("/renovar")){Renewals++;Expires=DateTimeOffset.UtcNow.AddMinutes(15);return Task.FromResult(receipts["renovar:PrepararFinanzas"]=Reservation());}
            if(path.EndsWith("/confirmar")){
                Confirmations++;ReservationState="Confirmada";receipts["confirmar"]=Reservation();
                if(LoseConfirmOnce&&!lost){lost=true;throw new HttpRequestException("response lost after commit");}
                return Task.FromResult(Reservation());
            }
            if(path.EndsWith("/liberar")){ReservationState="Liberada";return Task.FromResult(Reservation());}
            if(path.EndsWith("/preparar")){FinancePreparations++;return Task.FromResult(receipts["finance"]=Result(new{id=Operation,propiedad_id=1,venta_id=1,state="Preparada"}));}
            if(path.EndsWith("/activar")){Assert.Equal("Confirmada",ReservationState);FinanceActivations++;return Task.FromResult(receipts["finance"]=Result(new{id=Operation,propiedad_id=1,venta_id=1,state="Activa"}));}
            if(path.EndsWith("/abortar-preparacion"))return Task.FromResult(Result(new{state="Abortada"}));
            throw new InvalidOperationException("Unexpected test route: "+path);
        }
    }
    public static IEnumerable<object[]> Boundaries() {
        foreach(var step in new[]{"Oferta","Reservar","PrepararVenta","PrepararFinanzas","AutorizarConfirmacion","ConfirmarPropiedad","ActivarFinanzas","Completar"})
            foreach(var after in new[]{false,true})yield return new object[]{step,after};
    }
    private static ClaimsPrincipal Actor()=>new(new ClaimsIdentity(new[]{new Claim("iss","tests"),new Claim("sub","1")},"test"));
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Operations:WorkerEnabled"]="true",["Operations:Dataset"]="tests"}).Build();
    private static CoordinatedSale Request()=>new(Guid.NewGuid(),Guid.NewGuid(),1,1,1,new DateOnly(2026,11,4),"CONTADO",1000,1000);
    private static async Task Seed(AppDbContext db) {
        await db.Database.EnsureCreatedAsync();
        db.Clientes.Add(new Cliente{Ci="fixture",NombreCompleto="Cliente"});
        db.Asesores.Add(new Asesor{NombreCompleto="Asesor",PorcentajeComision=10});
        await db.SaveChangesAsync();
    }
    private static async Task Drain(AppDbContext db,SaleCoordinator service,Guid id) {
        for(var i=0;i<30;i++) {
            db.ChangeTracker.Clear();
            var op=await db.Set<SaleOperation>().FindAsync(id);
            if(op!.State=="Completada")return;
            op.LeaseUntil=DateTime.UtcNow.AddSeconds(-1);op.NextAttempt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
            await service.Run(default);
        }
        Assert.Fail("Did not recover operation.");
    }
    [Theory,MemberData(nameof(Boundaries))]
    public async Task CrashBeforeAndAfterEveryStepRecoversOneSale(string step,bool after) {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var remote=new Remote(Config());var crash=new Crash(step,after);var request=Request();
        await using(var first=new AppDbContext(options)) {
            await Seed(first);var coordinator=new SaleCoordinator(first,remote,Config(),crash);
            await coordinator.Begin(request,Actor(),"Bearer test-not-persisted",default);
            for(var i=0;i<15&&!crash.Fired;i++) {
                try{await coordinator.Run(default);}catch(InvalidOperationException)when(crash.Fired){}
            }
            Assert.True(crash.Fired);
        }
        await using var recovered=new AppDbContext(options);
        await Drain(recovered,new SaleCoordinator(recovered,remote,Config()),request.OperacionId);
        Assert.Equal(1,await recovered.Ventas.CountAsync());Assert.Equal(1,await recovered.Contratos.CountAsync());
        Assert.Equal("Activa",(await recovered.Ventas.SingleAsync()).Estado);
        Assert.Equal("Confirmada",remote.ReservationState);Assert.Equal(1,remote.FinancePreparations);Assert.Equal(1,remote.FinanceActivations);
        Assert.DoesNotContain("Bearer",JsonSerializer.Serialize(await recovered.Set<SaleOperation>().SingleAsync()));
    }
    [Fact]
    public async Task NearExpiryRenewsBeforePivotAndKeepsOneReservation() {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db=new AppDbContext(options);await Seed(db);
        var remote=new Remote(Config()){Expires=DateTimeOffset.UtcNow.AddSeconds(45)};
        var request=Request();var coordinator=new SaleCoordinator(db,remote,Config());
        await coordinator.Begin(request,Actor(),"Bearer fixture",default);await Drain(db,coordinator,request.OperacionId);
        Assert.Equal(1,remote.Renewals);Assert.Equal("Confirmada",remote.ReservationState);Assert.Equal(1,await db.Ventas.CountAsync());
    }
    [Fact]public async Task LostConfirmationResponseNeverCompensatesConfirmedProperty() {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await Seed(db);
        var remote=new Remote(Config()){LoseConfirmOnce=true};var service=new SaleCoordinator(db,remote,Config());var request=Request();
        await service.Begin(request,Actor(),"Bearer not-persisted",default);await Drain(db,service,request.OperacionId);
        Assert.Equal("Confirmada",remote.ReservationState);Assert.Equal(1,await db.Ventas.CountAsync());
        Assert.Equal(1,remote.Confirmations);
    }
    [Fact]public async Task SameUuidCannotChangeEconomicPayloadOrOwner() {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await Seed(db);
        var service=new SaleCoordinator(db,new Remote(Config()),Config());var request=Request();
        await service.Begin(request,Actor(),"Bearer not-persisted",default);
        await Assert.ThrowsAsync<MSMultilider.Comercial.Infrastructure.BusinessException>(()=>service.Begin(request with{MontoTotal=999},Actor(),"Bearer",default));
        var other=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("iss","tests"),new Claim("sub","2")},"test"));
        await Assert.ThrowsAsync<MSMultilider.Comercial.Infrastructure.BusinessException>(()=>service.Begin(request,other,"Bearer",default));
        Assert.Equal(1,await db.Set<SaleOperation>().CountAsync());
    }
}
