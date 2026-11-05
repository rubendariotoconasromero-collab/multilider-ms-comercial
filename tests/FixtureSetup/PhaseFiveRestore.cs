using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

internal static class PhaseFiveRestore {
    public static async Task Run() {
        var root=Path.GetFullPath(Environment.GetEnvironmentVariable("MULTILIDER_F5_BACKUP_ROOT")??throw new InvalidOperationException("Fixture backup root required."));
        var safeParent=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"..","backend","storage","framework","testing"));
        const string folderPrefix="f5-restore-";
        if(!string.Equals(Path.GetDirectoryName(root),safeParent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith(folderPrefix)||!Guid.TryParseExact(Path.GetFileName(root)[folderPrefix.Length..],"N",out _))
            throw new InvalidOperationException("Unsafe backup fixture root.");
        Directory.CreateDirectory(root);
        foreach(var (key,prefix,checks) in new[]{
            ("MULTILIDER_PROPERTIES_TEST_CONNECTION","multilider_properties_f3_test_",new[]{"propiedades"}),
            ("MULTILIDER_COMERCIAL_TEST_CONNECTION","multilider_comercial_f4_test_",new[]{"SaleOperation","OperationStep","DocumentRevision"}),
            ("MULTILIDER_FINANZAS_TEST_CONNECTION","multilider_finanzas_f5_test_",new[]{"FinancialOperation","FinancialCommand","ClosureRejection","Ingresos","Egresos"})
        }) {
            var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(key));var source=connection.InitialCatalog;
            if(connection.DataSource!="localhost"||!connection.IntegratedSecurity||!source.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(source[prefix.Length..],"N",out _))
                throw new InvalidOperationException("Fixture database guard failed.");
            var restored=prefix+Guid.NewGuid().ToString("N");
            var backup=Path.Combine(root,source+".bak");
            connection.InitialCatalog="master";
            await using var sql=new SqlConnection(connection.ConnectionString);await sql.OpenAsync();
            string Quote(string value)=>"N'"+value.Replace("'","''")+"'";
            async Task Exec(string statement) {await using var cmd=new SqlCommand(statement,sql){CommandTimeout=60};await cmd.ExecuteNonQueryAsync();}
            async Task<string> Scalar(string statement) {await using var cmd=new SqlCommand(statement,sql){CommandTimeout=30};return Convert.ToString(await cmd.ExecuteScalarAsync())??"";}
            async Task<string> Fingerprint(string database,string table) {
                await using var command=new SqlCommand($"SELECT * FROM [{database}].dbo.[{table}] ORDER BY 1 FOR JSON PATH, INCLUDE_NULL_VALUES",sql);
                await using var reader=await command.ExecuteReaderAsync();var buffer=new StringBuilder();while(await reader.ReadAsync())buffer.Append(reader.GetString(0));
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString())));
            }
            var data=await Scalar("SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath'))");
            var logs=await Scalar("SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath'))");
            if(!Path.IsPathFullyQualified(data)||!Path.IsPathFullyQualified(logs)||await Scalar($"SELECT DB_ID({Quote(restored)})")!="")throw new InvalidOperationException("Unsafe restore destination.");
            await Exec($"BACKUP DATABASE [{source}] TO DISK={Quote(backup)} WITH COPY_ONLY,CHECKSUM");
            var moves=new List<string>();
            await using(var fileList=new SqlCommand($"RESTORE FILELISTONLY FROM DISK={Quote(backup)}",sql))
            await using(var reader=await fileList.ExecuteReaderAsync()) {
                while(await reader.ReadAsync()) {
                    var type=reader.GetString(reader.GetOrdinal("Type"));var logical=reader.GetString(reader.GetOrdinal("LogicalName"));
                    if(type is not ("D" or "L"))throw new InvalidOperationException("Unsupported fixture database file.");
                    var destination=Path.Combine(type=="D"?data:logs,restored+"-"+moves.Count+(type=="D"?".mdf":".ldf"));
                    moves.Add($"MOVE {Quote(logical)} TO {Quote(destination)}");
                }
            }
            if(moves.Count!=2)throw new InvalidOperationException("Expected two files in test database.");
            try {
                await Exec($"RESTORE DATABASE [{restored}] FROM DISK={Quote(backup)} WITH CHECKSUM,RECOVERY,{string.Join(',',moves)}");
                foreach(var table in checks)if(await Fingerprint(source,table)!=await Fingerprint(restored,table))throw new InvalidOperationException("Restored fixture rows differ.");
                Console.WriteLine($"F5 SQL backup/restore verified: {prefix.TrimEnd('_')} ({checks.Length} table fingerprints).");
            } finally {
                if(!string.IsNullOrEmpty(await Scalar($"SELECT DB_ID({Quote(restored)})")))await Exec($"DROP DATABASE [{restored}]");
            }
        }
    }
}
