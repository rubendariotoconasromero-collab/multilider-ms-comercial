using System.Text.Json.Serialization;
using MSMultilider.Comercial.Infrastructure;
namespace MSMultilider.Comercial.Models;

public sealed class Cliente:Entity {
    public int? UserId {get;set;}
    public string Ci {get;set;}="";
    public string? LugarExpedicion {get;set;}
    public string NombreCompleto {get;set;}="";
    public string? Telefono {get;set;}
    public string? Correo {get;set;}
    public string? Direccion {get;set;}
    public bool Estado {get;set;}=true;
}
public sealed class Asesor:Entity {
    public int? UserId {get;set;}
    public string NombreCompleto {get;set;}="";
    public string? Telefono {get;set;}
    public string? Correo {get;set;}
    public string? Direccion {get;set;}
    public bool Estado {get;set;}=true;
    public decimal PorcentajeComision {get;set;}
}
public sealed class Venta:Entity {
    public Guid SolicitudId {get;set;}
    [JsonIgnore] public string SolicitudHash {get;set;}="";
    public int ClienteId {get;set;}
    public int AsesorId {get;set;}
    public int PropiedadId {get;set;}
    [JsonIgnore] public int? PropiedadActivaId {get;set;}
    public string CodigoPropiedad {get;set;}="";
    public string Moneda {get;set;}="BOB";
    public DateOnly Fecha {get;set;}
    public string TipoVenta {get;set;}="";
    public decimal MontoTotal {get;set;}
    public decimal Descuento {get;set;}
    public decimal MontoLiquido {get;set;}
    public decimal MontoComision {get;set;}
    public decimal CuotaInicial {get;set;}
    public decimal SaldoCredito {get;set;}
    public int NumeroCuotas {get;set;}
    public decimal TasaInteres {get;set;}
    public decimal TasaMora {get;set;}
    public DateOnly? FechaInicioPago {get;set;}
    public string Estado {get;set;}="Activa";
    public Cliente Cliente {get;set;}=null!;
    public Asesor Asesor {get;set;}=null!;
}
public sealed class Contrato:Entity {
    public int NotaVentaId {get;set;}
    public string CodigoContrato {get;set;}="";
    public DateOnly FechaEmision {get;set;}
    public DateOnly? FechaFirma {get;set;}
    public string Estado {get;set;}="Pendiente";
    [JsonIgnore] public string? DocumentoId {get;set;}
    public Venta NotaVenta {get;set;}=null!;
}
public sealed class Entrega:Entity {
    public int ContratoId {get;set;}
    public DateOnly FechaProgramada {get;set;}
    public DateOnly? FechaEntrega {get;set;}
    public string Estado {get;set;}="Pendiente";
    public string? CondicionInmueble {get;set;}
    public string? ItemsEntregados {get;set;}
    public string? Observaciones {get;set;}
    public string? EntregadoPor {get;set;}
    public string? RecibidoPor {get;set;}
    [JsonIgnore] public string? ActaId {get;set;}
    public Contrato Contrato {get;set;}=null!;
}

