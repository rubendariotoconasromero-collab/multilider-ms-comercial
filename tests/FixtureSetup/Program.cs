using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
if(Environment.GetEnvironmentVariable("MULTILIDER_TEST_ISOLATED")!="1")throw new InvalidOperationException("Isolated runner required.");
if(args.Contains("verify-restoration")){await PhaseFiveRestore.Run();return;}
if(args is ["migration-manifest",var assemblyPath]) {
    var context=new System.Runtime.Loader.AssemblyLoadContext("ArchivedComercial",isCollectible:true);
    var archive=context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
    var migrations=archive.GetTypes().Where(x=>typeof(Migration).IsAssignableFrom(x))
        .Select(x=>x.GetCustomAttributes(typeof(MigrationAttribute),false).OfType<MigrationAttribute>().SingleOrDefault()?.Id)
        .Where(x=>x is not null).Order();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(migrations));
    return;
}
var connection=Environment.GetEnvironmentVariable("MULTILIDER_COMERCIAL_TEST_CONNECTION");
var builder=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);const string prefix="multilider_comercial_f4_test_";
if(builder.DataSource!="localhost"||!builder.InitialCatalog.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(builder.InitialCatalog[prefix.Length..],"N",out _))throw new InvalidOperationException("Unsafe test database.");
await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
if(args.Contains("drop")){await db.Database.EnsureDeletedAsync();Console.WriteLine("Temporary Comercial database removed.");return;}
if(args.Contains("upgrade-f4")) {
    await db.GetService<IMigrator>().MigrateAsync("20261104235150_AvailabilityIntents");
    var id=Guid.NewGuid();var command=Guid.NewGuid();const string json="{}";
    await db.Database.ExecuteSqlInterpolatedAsync($$"""
        INSERT dbo.AvailabilityIntents (Id,ActorIssuer,ActorSubject,PropiedadId,ClienteId,AsesorId,CreatedAt,UpdatedAt)
        VALUES ({{id}},N'fixture-f4',N'99',123,99,99,SYSUTCDATETIME(),SYSUTCDATETIME());
        INSERT dbo.AvailabilityCommands (Id,IntentId,Action,RequestHash,Payload,State,HttpStatus,Result,CreatedAt,UpdatedAt)
        VALUES ({{command}},{{id}},N'reservar',REPLICATE(N'A',64),{{json}},N'Completada',200,{{json}},SYSUTCDATETIME(),SYSUTCDATETIME());
        """);
    await db.GetService<IMigrator>().MigrateAsync("20261105052212_BoundProfileRecovery");
    var profileId=Guid.NewGuid();
    var historicalPayload="""{"ci":"F5-HISTORICAL-TEST","correo":"fixture@example.test","estado":false}""";
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT dbo.ProfileOperation (Id,Kind,ResourceId,State,ActorIssuer,ActorSubject,Payload,PayloadHash,Attempts,NextAttempt,UpdatedAt)
        VALUES ({profileId},N'cliente',99,N'Completada',N'fixture',N'99',{historicalPayload},REPLICATE(N'B',64),0,SYSUTCDATETIME(),SYSUTCDATETIME());
        """);
    await ComercialMigrations.Apply(db,builder.InitialCatalog);
    var old=await db.AvailabilityIntents.SingleAsync(x=>x.Id==id);
    var receipt=await db.AvailabilityCommands.SingleAsync(x=>x.Id==command);
    if(old.PropiedadId!=123||old.ActorIssuer!="fixture-f4"||receipt.IntentId!=id||receipt.HttpStatus!=200||receipt.RequestHash!=new string('A',64))
        throw new InvalidOperationException("F5 upgrade changed the F4 intent or receipt.");
    var cleaned=await db.Set<ProfileOperation>().SingleAsync(x=>x.Id==profileId);
    if(cleaned.Payload!="{\"estado\":false}"||cleaned.State!="Completada"||cleaned.PayloadHash!=new string('B',64))
        throw new InvalidOperationException("Profile retry data was not minimized without changing its receipt.");
    Console.WriteLine("F5-O05: Comercial preserved a populated F4 reservation intent and receipt.");
} else {
    await db.Database.MigrateAsync();
}
if(args.Contains("verify")){if(await db.Ventas.AnyAsync()||await db.Contratos.AnyAsync()||!await db.AvailabilityCommands.AnyAsync())throw new InvalidOperationException("Unexpected sales or missing intents.");Console.WriteLine("Durable intentions verified; no sale or contract created.");return;}
db.Clientes.Add(new(){Ci="F4-CLIENT",NombreCompleto="Cliente F4"});db.Asesores.Add(new(){NombreCompleto="Asesor F4",PorcentajeComision=3});await db.SaveChangesAsync();Console.WriteLine("Unique Comercial SQL fixture prepared.");
