using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoundProfileRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "ProfileOperation",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttempt",
                table: "ProfileOperation",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.InvalidOperationException("F5 durable records require coordinated restore instead of a destructive down migration.");
        }
    }
}
