using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSMultilider.Comercial.Services;
namespace MSMultilider.Comercial.Controllers;
[ApiController,Route("api/disponibilidad/operaciones"),Authorize(Policy="acceso_ventas")]
public sealed class AvailabilityController(AvailabilityService service):ControllerBase {
    [HttpPost]public async Task<IActionResult> Prepare(PrepareAvailability r,CancellationToken ct){var result=await service.Prepare(r,ct);return StatusCode(result.Status,result.Body);}
    [HttpGet("{id:guid}")]public async Task<IActionResult> Get(Guid id,[FromQuery(Name="command_id")]Guid? commandId,CancellationToken ct){var result=await service.Get(id,ct,commandId);return StatusCode(result.Item1,result.Item2);}
    [HttpPost("{id:guid}/{transition}")]public async Task<IActionResult> Change(Guid id,string transition,ChangeAvailability r,CancellationToken ct){var result=await service.Change(id,transition,r,ct);return StatusCode(result.Status,result.Body);}
}
