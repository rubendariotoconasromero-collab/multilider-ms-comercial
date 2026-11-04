using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
using Xunit;
namespace MSMultilider.Comercial.Tests;
public sealed class SqlServerFactAttribute:FactAttribute {
    public SqlServerFactAttribute() {
        if(Environment.GetEnvironmentVariable("MULTILIDER_SQLSERVER_TESTS")!="1")
            Skip="Prueba opcional: MULTILIDER_SQLSERVER_TESTS=1; usa una base temporal nueva en SQL Server local.";
    }
}
public sealed class SqlServerMigrationTests {
    [SqlServerFact]
    public async Task MigracionesIndicesYConcurrenciaEnSqlServerReal() {
        const string prefix="multilider_comercial_test_";
        var name=prefix+Guid.NewGuid().ToString("N");
        var connection=new SqlConnectionStringBuilder("Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10");
        connection.InitialCatalog=name;
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db=new AppDbContext(options);
        try {
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var row=new Cliente {Ci="sql-test",NombreCompleto="SQL test"};db.Clientes.Add(row);await db.SaveChangesAsync();
            db.Clientes.Add(new Cliente {Ci="sql-test",NombreCompleto="Duplicate"});
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            await using var other=new AppDbContext(options);
            var first=await db.Clientes.SingleAsync();var stale=await other.Clientes.SingleAsync();
            first.NombreCompleto="First";stale.NombreCompleto="Stale";
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>other.SaveChangesAsync());
        } finally {
            // This test only drops the unique, newly created test database.
            if(!name.StartsWith(prefix,StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..],"N",out _))
                throw new InvalidOperationException("Unexpected test database name.");
            await db.Database.EnsureDeletedAsync();
        }
    }
}

