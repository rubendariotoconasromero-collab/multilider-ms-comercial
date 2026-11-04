using System.ComponentModel.DataAnnotations;
namespace MSMultilider.Comercial.DTOs;
public sealed record ClienteRequest(
    [Required,StringLength(50)] string Ci,[Required,StringLength(150)] string NombreCompleto,
    [Range(1,int.MaxValue)] int? UserId=null,[StringLength(30)] string? LugarExpedicion=null,
    [StringLength(50)] string? Telefono=null,[EmailAddress,StringLength(254)] string? Correo=null,
    [StringLength(500)] string? Direccion=null,bool Estado=true);
public sealed record AsesorRequest(
    [Required,StringLength(150)] string NombreCompleto,[Range(typeof(decimal),"0","100",ParseLimitsInInvariantCulture=true,ConvertValueInInvariantCulture=true)] decimal PorcentajeComision,
    [Range(1,int.MaxValue)] int? UserId=null,[StringLength(50)] string? Telefono=null,
    [EmailAddress,StringLength(254)] string? Correo=null,[StringLength(500)] string? Direccion=null,bool Estado=true);
public sealed record VentaRequest(
    Guid SolicitudId,[Range(1,int.MaxValue)] int ClienteId,[Range(1,int.MaxValue)] int AsesorId,
    [Range(1,int.MaxValue)] int PropiedadId,DateOnly Fecha,[Required] string TipoVenta,
    [Range(typeof(decimal),"0","1000000000000",ParseLimitsInInvariantCulture=true,ConvertValueInInvariantCulture=true)] decimal Descuento=0,
    [Range(typeof(decimal),"0","1000000000000",ParseLimitsInInvariantCulture=true,ConvertValueInInvariantCulture=true)] decimal CuotaInicial=0,
    [Range(0,600)] int NumeroCuotas=0,[Range(typeof(decimal),"0","100",ParseLimitsInInvariantCulture=true,ConvertValueInInvariantCulture=true)] decimal TasaInteres=0,
    [Range(typeof(decimal),"0","100",ParseLimitsInInvariantCulture=true,ConvertValueInInvariantCulture=true)] decimal TasaMora=0,DateOnly? FechaInicioPago=null);
public sealed record FirmarContratoRequest(DateOnly FechaFirma,DateOnly FechaProgramadaEntrega);
public sealed record EntregaRequest(
    [Required] string Estado,DateOnly FechaProgramada,DateOnly? FechaEntrega=null,
    [StringLength(100)] string? CondicionInmueble=null,[StringLength(1000)] string? ItemsEntregados=null,
    [StringLength(1000)] string? Observaciones=null,[StringLength(150)] string? EntregadoPor=null,[StringLength(150)] string? RecibidoPor=null);
public sealed record PropiedadSnapshot(int Id,string Codigo,decimal PrecioVenta,string Moneda,string Estado,bool Activo);
public sealed record VentaFinanciera(int Id,int ClienteId,int AsesorId,int PropiedadId,DateOnly Fecha,string Estado,
    string TipoVenta,string Moneda,decimal MontoLiquido,decimal CuotaInicial,decimal SaldoCredito,
    decimal MontoComision,int NumeroCuotas,decimal TasaInteres,decimal TasaMora,DateOnly? FechaInicioPago);

