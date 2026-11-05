using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
if(Environment.GetEnvironmentVariable("MULTILIDER_TEST_ISOLATED")!="1")throw new InvalidOperationException("Isolated runner required.");
if(args.Contains("verify-restoration")){await PhaseFiveRestore.Run();return;}
var connection=Environment.GetEnvironmentVariable("MULTILIDER_COMERCIAL_TEST_CONNECTION");
var builder=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);const string prefix="multilider_comercial_f4_test_";
if(builder.DataSource!="localhost"||!builder.InitialCatalog.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(builder.InitialCatalog[prefix.Length..],"N",out _))throw new InvalidOperationException("Unsafe test database.");
await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
if(args.Contains("drop")){await db.Database.EnsureDeletedAsync();Console.WriteLine("Temporary Comercial database removed.");return;}
await db.Database.MigrateAsync();
if(args.Contains("verify")){if(await db.Ventas.AnyAsync()||await db.Contratos.AnyAsync()||!await db.AvailabilityCommands.AnyAsync())throw new InvalidOperationException("Unexpected sales or missing intents.");Console.WriteLine("Durable intentions verified; no sale or contract created.");return;}
db.Clientes.Add(new(){Ci="F4-CLIENT",NombreCompleto="Cliente F4"});db.Asesores.Add(new(){NombreCompleto="Asesor F4",PorcentajeComision=3});await db.SaveChangesAsync();Console.WriteLine("Unique Comercial SQL fixture prepared.");
