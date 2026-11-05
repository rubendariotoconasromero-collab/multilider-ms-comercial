using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class DurableSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationStep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationStep", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SaleOperation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PropiedadId = table.Column<int>(type: "int", nullable: false),
                    VentaId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Step = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorIssuer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorSubject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Offer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reservation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttempt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleOperation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationStep_OperationId_Step",
                table: "OperationStep",
                columns: new[] { "OperationId", "Step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleOperation_State_NextAttempt",
                table: "SaleOperation",
                columns: new[] { "State", "NextAttempt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.InvalidOperationException("F5 durable records require coordinated restore instead of a destructive down migration.");
        }
    }
}
