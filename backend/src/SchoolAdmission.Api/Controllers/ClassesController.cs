using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("classes")]
public class ClassesController : ControllerBase
{
    private readonly AdmissionService _service;

    public ClassesController(AdmissionService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClassRequest request, CancellationToken ct)
    {
        var result = await _service.CreateClassAsync(request, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Invalid request."));
        return StatusCode(result.StatusCode, result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var classes = await _service.ListClassesAsync(ct);
        return Ok(classes);
    }

    [HttpGet("{classId}")]
    public async Task<IActionResult> Get(string classId, CancellationToken ct)
    {
        var result = await _service.GetClassAsync(classId, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Not found."));
        return Ok(result.Value);
    }

    [HttpPost("{classId}/holds")]
    public async Task<IActionResult> CreateHold(string classId, [FromHeader(Name = "X-Student-Id")] string? studentId, CancellationToken ct)
    {
        var result = await _service.CreateHoldAsync(classId, studentId ?? string.Empty, ct);
        if (!result.Success)
            return StatusCode(result.StatusCode, new ErrorResponse(result.Error ?? "Unable to create hold."));
        return StatusCode(result.StatusCode, result.Value);
    }
}
