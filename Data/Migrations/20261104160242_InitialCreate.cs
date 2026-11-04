using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSMultilider.Comercial.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Asesores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    NombreCompleto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Estado = table.Column<bool>(type: "bit", nullable: false),
                    PorcentajeComision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asesores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Ci = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LugarExpedicion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NombreCompleto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Estado = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ventas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SolicitudId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SolicitudHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    AsesorId = table.Column<int>(type: "int", nullable: false),
                    PropiedadId = table.Column<int>(type: "int", nullable: false),
                    PropiedadActivaId = table.Column<int>(type: "int", nullable: true),
                    CodigoPropiedad = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Moneda = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    TipoVenta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MontoTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoLiquido = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoComision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CuotaInicial = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoCredito = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NumeroCuotas = table.Column<int>(type: "int", nullable: false),
                    TasaInteres = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TasaMora = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaInicioPago = table.Column<DateOnly>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ventas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ventas_Asesores_AsesorId",
                        column: x => x.AsesorId,
                        principalTable: "Asesores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ventas_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contratos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotaVentaId = table.Column<int>(type: "int", nullable: false),
                    CodigoContrato = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaFirma = table.Column<DateOnly>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DocumentoId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contratos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contratos_Ventas_NotaVentaId",
                        column: x => x.NotaVentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Entregas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContratoId = table.Column<int>(type: "int", nullable: false),
                    FechaProgramada = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaEntrega = table.Column<DateOnly>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CondicionInmueble = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ItemsEntregados = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EntregadoPor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecibidoPor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActaId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entregas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entregas_Contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "Contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Ci",
                table: "Clientes",
                column: "Ci",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_CodigoContrato",
                table: "Contratos",
                column: "CodigoContrato",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_NotaVentaId",
                table: "Contratos",
                column: "NotaVentaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entregas_ContratoId",
                table: "Entregas",
                column: "ContratoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_AsesorId",
                table: "Ventas",
                column: "AsesorId");

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_ClienteId",
                table: "Ventas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_PropiedadActivaId",
                table: "Ventas",
                column: "PropiedadActivaId",
                unique: true,
                filter: "[PropiedadActivaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_SolicitudId",
                table: "Ventas",
                column: "SolicitudId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entregas");

            migrationBuilder.DropTable(
                name: "Contratos");

            migrationBuilder.DropTable(
                name: "Ventas");

            migrationBuilder.DropTable(
                name: "Asesores");

            migrationBuilder.DropTable(
                name: "Clientes");
        }
    }
}
