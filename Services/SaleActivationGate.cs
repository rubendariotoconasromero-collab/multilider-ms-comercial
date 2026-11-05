using MSMultilider.Comercial.Infrastructure;
namespace MSMultilider.Comercial.Services;
public interface ISaleActivationGate { void RequireReady(); }
public sealed class SaleActivationGate:ISaleActivationGate {
    public void RequireReady()=>throw new BusinessException("INTEGRATION_NOT_READY: la coordinación de venta, contrato y Finanzas corresponde a F5.",503);
}
