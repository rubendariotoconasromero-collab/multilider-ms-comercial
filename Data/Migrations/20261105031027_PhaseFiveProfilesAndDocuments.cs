using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFiveProfilesAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentSnapshot",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Step",
                table: "SaleOperation",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "SaleOperation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "SaleOperation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "Step",
                table: "OperationStep",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.CreateTable(
                name: "DocumentRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    StorageId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevision", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileOperation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorIssuer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorSubject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileOperation", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.InvalidOperationException("F5 durable records require coordinated restore instead of a destructive down migration.");
        }
    }
}
