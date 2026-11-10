using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
public class HoldsController : ControllerBase
{
    private readonly AdmissionService _service;

    public HoldsController(AdmissionService service)
    {
        _service = service;
    }

    [HttpGet("holds/{holdId}")]
    public async Task<IActionResult> Get(string holdId, CancellationToken ct)
    {
        var result = await _service.GetHoldAsync(holdId, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Not found."));
        return Ok(result.Value);
    }

    [HttpPost("holds/{holdId}/confirm")]
    public async Task<IActionResult> Confirm(string holdId, [FromHeader(Name = "X-Student-Id")] string? studentId, CancellationToken ct)
    {
        var result = await _service.ConfirmHoldAsync(holdId, studentId ?? string.Empty, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Unable to confirm application."));
        return Ok(result.Value);
    }

    [HttpDelete("holds/{holdId}")]
    public async Task<IActionResult> Cancel(string holdId, [FromHeader(Name = "X-Student-Id")] string? studentId, CancellationToken ct)
    {
        var result = await _service.CancelHoldAsync(holdId, studentId ?? string.Empty, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Unable to cancel application."));
        return NoContent();
    }
}
