using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePhotoAndProtocolLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityIssuer",
                table: "Clientes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentitySubject",
                table: "Clientes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityIssuer",
                table: "Asesores",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentitySubject",
                table: "Asesores",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoId",
                table: "Asesores",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoMime",
                table: "Asesores",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.InvalidOperationException("F5 durable records require coordinated restore instead of a destructive down migration.");
        }
    }
}
