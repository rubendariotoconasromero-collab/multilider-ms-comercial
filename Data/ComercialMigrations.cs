using Microsoft.EntityFrameworkCore;
namespace MSMultilider.Comercial.Data;
public static class ComercialMigrations {
    public static async Task Apply(AppDbContext db,string expectedDatabase,CancellationToken ct=default) {
        if(!db.Database.IsSqlServer()||string.IsNullOrWhiteSpace(expectedDatabase)||db.Database.GetDbConnection().Database!=expectedDatabase)throw new InvalidOperationException("An explicit matching migration database is required.");
        await db.Database.OpenConnectionAsync(ct);
        var applied=(await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if(applied.Except(db.Database.GetMigrations()).Any())throw new InvalidOperationException("Unknown migration history; stop and inspect the backup.");
        if(applied.Length==0) {
            await using var command=db.Database.GetDbConnection().CreateCommand();command.CommandText="SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0 AND name<>'__EFMigrationsHistory'";
            if(Convert.ToInt32(await command.ExecuteScalarAsync(ct))!=0)throw new InvalidOperationException("A nonempty Comercial schema without migration history requires review.");
        }
        await db.Database.CloseConnectionAsync();await db.Database.MigrateAsync(ct);
    }
}
