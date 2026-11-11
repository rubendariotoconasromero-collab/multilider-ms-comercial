using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MSMultilider.Comercial.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261111000100_MinimizeProfileRetryPayload")]
public sealed class MinimizeProfileRetryPayload : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE dbo.ProfileOperation
            SET Payload = CASE
                WHEN NOT EXISTS (
                    SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(Payload) = 1 THEN Payload ELSE N'{}' END)
                    WHERE [key] = N'estado'
                ) THEN N'{}'
                WHEN JSON_VALUE(Payload, '$.estado') IN (N'true', N'1') THEN N'{"estado":true}'
                ELSE N'{"estado":false}'
            END
            WHERE ISJSON(Payload) = 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Discarded personal data belongs to the profile and is not restored to the retry queue.
    }
}
