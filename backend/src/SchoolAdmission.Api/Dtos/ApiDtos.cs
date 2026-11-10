using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.Dtos;

public record CreateClassRequest(string? Name, int TotalSeats);

public record CreateClassResponse(string Id, string Name, int TotalSeats);

public record ClassResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);

public record CreateHoldResponse(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    DateTime ExpiresAt);

public record HoldResponse(
    string HoldId,
    string ClassId,
    string Status,
    DateTime ExpiresAt);

public record ConfirmHoldResponse(string HoldId, string Status);

public record MyHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    DateTime ExpiresAt);

public record ErrorResponse([property: JsonPropertyName("error")] string Error);
