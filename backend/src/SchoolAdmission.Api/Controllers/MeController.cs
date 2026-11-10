using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
public class MeController : ControllerBase
{
    private readonly AdmissionService _service;

    public MeController(AdmissionService service)
    {
        _service = service;
    }

    [HttpGet("me/holds")]
    public async Task<IActionResult> GetMyHolds([FromHeader(Name = "X-Student-Id")] string? studentId, CancellationToken ct)
    {
        var result = await _service.GetMyHoldsAsync(studentId ?? string.Empty, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Unable to fetch applications."));
        return Ok(result.Value);
    }
}
