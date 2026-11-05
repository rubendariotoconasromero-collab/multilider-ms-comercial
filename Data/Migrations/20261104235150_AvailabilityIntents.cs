using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AvailabilityIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvailabilityIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ActorSubject = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    PropiedadId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    AsesorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityIntents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilityCommands_AvailabilityIntents_IntentId",
                        column: x => x.IntentId,
                        principalTable: "AvailabilityIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityCommands_IntentId_CreatedAt",
                table: "AvailabilityCommands",
                columns: new[] { "IntentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityCommands");

            migrationBuilder.DropTable(
                name: "AvailabilityIntents");
        }
    }
}
