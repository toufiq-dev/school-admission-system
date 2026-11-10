using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Services;

public record ServiceResult<T>(bool Success, int StatusCode, T? Value, string? Error)
{
    public static ServiceResult<T> Ok(T value, int statusCode = 200) => new(true, statusCode, value, null);
    public static ServiceResult<T> Fail(int statusCode, string error) => new(false, statusCode, default, error);
}

public class AdmissionService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly int _holdDurationSeconds;

    public AdmissionService(AppDbContext db, TimeProvider timeProvider, IConfiguration config)
    {
        _db = db;
        _timeProvider = timeProvider;
        var raw = config["HOLD_DURATION_SECONDS"] ?? Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS");
        _holdDurationSeconds = int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : 600;
    }

    public int HoldDurationSeconds => _holdDurationSeconds;

    public async Task<ServiceResult<CreateClassResponse>> CreateClassAsync(CreateClassRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<CreateClassResponse>.Fail(400, "Class name is required.");
        if (request.TotalSeats <= 0)
            return ServiceResult<CreateClassResponse>.Fail(400, "Total seats must be greater than zero.");

        var entity = new AdmissionClass
        {
            Id = $"c_{Guid.NewGuid():N}",
            Name = name,
            TotalSeats = request.TotalSeats,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        _db.Classes.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<CreateClassResponse>.Ok(new CreateClassResponse(entity.Id, entity.Name, entity.TotalSeats), 201);
    }

    public async Task<List<ClassResponse>> ListClassesAsync(CancellationToken ct)
    {
        await ExpireOverdueHoldsAsync(ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var classes = await _db.Classes.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var classIds = classes.Select(c => c.Id).ToList();
        if (classIds.Count == 0) return new List<ClassResponse>();

        var counts = await _db.Holds.AsNoTracking()
            .Where(h => classIds.Contains(h.ClassId))
            .GroupBy(h => h.ClassId)
            .Select(g => new
            {
                ClassId = g.Key,
                Confirmed = g.Count(h => h.Status == HoldStatus.Confirmed),
                Held = g.Count(h => h.Status == HoldStatus.Held && h.ExpiresAt > now)
            })
            .ToListAsync(ct);

        var map = counts.ToDictionary(x => x.ClassId);
        return classes.Select(c =>
        {
            map.TryGetValue(c.Id, out var cts);
            var held = cts?.Held ?? 0;
            var confirmed = cts?.Confirmed ?? 0;
            return new ClassResponse(c.Id, c.Name, c.TotalSeats, held, confirmed, Math.Max(0, c.TotalSeats - held - confirmed));
        }).ToList();
    }

    public async Task<ServiceResult<ClassResponse>> GetClassAsync(string classId, CancellationToken ct)
    {
        await ExpireOverdueHoldsAsync(ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cls = await _db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls == null)
            return ServiceResult<ClassResponse>.Fail(404, "Class not found.");

        var confirmed = await _db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Confirmed, ct);
        var held = await _db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Held && h.ExpiresAt > now, ct);
        return ServiceResult<ClassResponse>.Ok(new ClassResponse(cls.Id, cls.Name, cls.TotalSeats, held, confirmed, Math.Max(0, cls.TotalSeats - held - confirmed)));
    }

    public async Task<ServiceResult<CreateHoldResponse>> CreateHoldAsync(string classId, string studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            return ServiceResult<CreateHoldResponse>.Fail(400, "X-Student-Id header is required.");
        studentId = studentId.Trim();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var cls = await _db.Classes
                .FromSqlRaw("SELECT * FROM classes WHERE id = {0} FOR UPDATE", classId)
                .AsTracking()
                .FirstOrDefaultAsync(ct);
            if (cls == null)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<CreateHoldResponse>.Fail(404, "Class not found.");
            }

            var overdue = await _db.Holds
                .Where(h => h.ClassId == classId && h.Status == HoldStatus.Held && h.ExpiresAt <= now)
                .ToListAsync(ct);
            foreach (var h in overdue)
            {
                h.Status = HoldStatus.Expired;
                h.UpdatedAt = now;
            }
            if (overdue.Count > 0)
                await _db.SaveChangesAsync(ct);

            var existing = await _db.Holds.AnyAsync(h =>
                h.ClassId == classId && h.StudentId == studentId &&
                (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now)), ct);
            if (existing)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<CreateHoldResponse>.Fail(409, "Student already has an active or confirmed application for this class.");
            }

            var confirmed = await _db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Confirmed, ct);
            var held = await _db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Held && h.ExpiresAt > now, ct);
            if (held + confirmed >= cls.TotalSeats)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<CreateHoldResponse>.Fail(409, "No seats are available for this class.");
            }

            var hold = new Hold
            {
                Id = $"h_{Guid.NewGuid():N}",
                ClassId = cls.Id,
                StudentId = studentId,
                Status = HoldStatus.Held,
                ExpiresAt = now.AddSeconds(_holdDurationSeconds),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Holds.Add(hold);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return ServiceResult<CreateHoldResponse>.Ok(
                new CreateHoldResponse(hold.Id, hold.ClassId, hold.StudentId, hold.Status, hold.ExpiresAt), 201);
        });
    }

    public async Task<ServiceResult<HoldResponse>> GetHoldAsync(string holdId, CancellationToken ct)
    {
        var hold = await _db.Holds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold == null)
            return ServiceResult<HoldResponse>.Fail(404, "Application not found.");

        await SyncHoldExpirationAsync(hold, ct);
        return ServiceResult<HoldResponse>.Ok(new HoldResponse(hold.Id, hold.ClassId, hold.Status, hold.ExpiresAt));
    }

    public async Task<ServiceResult<ConfirmHoldResponse>> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            return ServiceResult<ConfirmHoldResponse>.Fail(400, "X-Student-Id header is required.");
        studentId = studentId.Trim();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var hold = await _db.Holds
                .FromSqlRaw("SELECT * FROM holds WHERE id = {0} FOR UPDATE", holdId)
                .AsTracking()
                .FirstOrDefaultAsync(ct);
            if (hold == null)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<ConfirmHoldResponse>.Fail(404, "Application not found.");
            }

            if (hold.StudentId != studentId)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<ConfirmHoldResponse>.Fail(403, "You can only confirm your own application.");
            }

            if (hold.Status == HoldStatus.Confirmed)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<ConfirmHoldResponse>.Ok(new ConfirmHoldResponse(hold.Id, HoldStatus.Confirmed));
            }

            if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= now)
            {
                hold.Status = HoldStatus.Expired;
                hold.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ServiceResult<ConfirmHoldResponse>.Fail(409, "Application has expired.");
            }

            if (hold.Status != HoldStatus.Held)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<ConfirmHoldResponse>.Fail(409, "Only an unexpired held application can be confirmed.");
            }

            hold.Status = HoldStatus.Confirmed;
            hold.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ServiceResult<ConfirmHoldResponse>.Ok(new ConfirmHoldResponse(hold.Id, HoldStatus.Confirmed));
        });
    }

    public async Task<ServiceResult<bool>> CancelHoldAsync(string holdId, string studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            return ServiceResult<bool>.Fail(400, "X-Student-Id header is required.");
        studentId = studentId.Trim();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hold = await _db.Holds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold == null)
            return ServiceResult<bool>.Fail(404, "Application not found.");
        if (hold.StudentId != studentId)
            return ServiceResult<bool>.Fail(403, "You can only cancel your own application.");

        if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= now)
        {
            hold.Status = HoldStatus.Expired;
            hold.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return ServiceResult<bool>.Fail(409, "Application has expired and cannot be cancelled.");
        }

        if (hold.Status != HoldStatus.Held)
            return ServiceResult<bool>.Fail(409, "Only an active held application can be cancelled.");

        hold.Status = HoldStatus.Cancelled;
        hold.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Ok(true, 204);
    }

    public async Task<ServiceResult<List<MyHoldResponse>>> GetMyHoldsAsync(string studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            return ServiceResult<List<MyHoldResponse>>.Fail(400, "X-Student-Id header is required.");
        studentId = studentId.Trim();

        await ExpireOverdueHoldsAsync(ct);
        var holds = await _db.Holds
            .AsNoTracking()
            .Include(h => h.Class)
            .Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new MyHoldResponse(h.Id, h.ClassId, h.Class.Name, h.Status, h.ExpiresAt))
            .ToListAsync(ct);
        return ServiceResult<List<MyHoldResponse>>.Ok(holds);
    }

    public async Task<int> ExpireOverdueHoldsAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await _db.Holds
            .Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(h => h.Status, HoldStatus.Expired)
                .SetProperty(h => h.UpdatedAt, now), ct);
    }

    private async Task SyncHoldExpirationAsync(Hold hold, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= now)
        {
            hold.Status = HoldStatus.Expired;
            hold.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }
    }
}
