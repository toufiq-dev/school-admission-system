using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SchoolAdmission.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=school_admission_test;Username=postgres;Password=postgres";

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);

        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("HOLD_DURATION_SECONDS", "2");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["HOLD_DURATION_SECONDS"] = "2"
            });
        });
    }
}

public class ApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string StudentId(int n) => n.ToString("D17");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> CreateClassAsync(string? name = null, int totalSeats = 5)
    {
        name ??= $"Class {Guid.NewGuid():N}";
        var res = await _client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ReadJson(res);
    }

    private async Task<JsonElement> CreateHoldAsync(string classId, string studentId, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        req.Headers.Add("X-Student-Id", studentId);
        var res = await _client.SendAsync(req);
        Assert.Equal(expected, res.StatusCode);
        return res.Content.Headers.ContentLength == 0 ? default : await ReadJson(res);
    }

    [Fact]
    public async Task CreateAndListClasses_ReturnsExpectedSeatCounts()
    {
        var uniqueName = $"Class {Guid.NewGuid():N}";
        var created = await CreateClassAsync(uniqueName, 5);
        Assert.Equal(uniqueName, created.GetProperty("name").GetString());
        Assert.Equal(5, created.GetProperty("totalSeats").GetInt32());
        var classId = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(classId));

        var listRes = await _client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var items = await ReadJson(listRes);
        var match = items.EnumerateArray().First(x => x.GetProperty("id").GetString() == classId);
        Assert.Equal(5, match.GetProperty("totalSeats").GetInt32());
        Assert.Equal(0, match.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, match.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, match.GetProperty("availableSeats").GetInt32());

        var getRes = await _client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var single = await ReadJson(getRes);
        Assert.Equal(classId, single.GetProperty("id").GetString());
        Assert.Equal(uniqueName, single.GetProperty("name").GetString());
    }

    [Fact]
    public async Task StudentId_MustBeExactly17Digits()
    {
        var cls = await CreateClassAsync(totalSeats: 3);
        var classId = cls.GetProperty("id").GetString()!;

        foreach (var bad in new[] { "123", "1234567890123456", "123456789012345678", "2015269123456789A", "2015-26912345678", "" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            if (bad.Length > 0) req.Headers.Add("X-Student-Id", bad);
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        meReq.Headers.Add("X-Student-Id", "short");
        var meRes = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.BadRequest, meRes.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_IsIdempotent_And_MyHolds_IncludesConfirmed()
    {
        var cls = await CreateClassAsync(totalSeats: 2);
        var classId = cls.GetProperty("id").GetString()!;
        var className = cls.GetProperty("name").GetString()!;
        var studentId = "20152691234567001";

        var hold = await CreateHoldAsync(classId, studentId);
        var holdId = hold.GetProperty("holdId").GetString()!;
        Assert.Equal("HELD", hold.GetProperty("status").GetString());
        Assert.Equal(studentId, hold.GetProperty("studentId").GetString());

        using var confirm1Req = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirm1Req.Headers.Add("X-Student-Id", studentId);
        var confirm1 = await _client.SendAsync(confirm1Req);
        Assert.Equal(HttpStatusCode.OK, confirm1.StatusCode);
        var c1 = await ReadJson(confirm1);
        Assert.Equal("CONFIRMED", c1.GetProperty("status").GetString());

        using var confirm2Req = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirm2Req.Headers.Add("X-Student-Id", studentId);
        var confirm2 = await _client.SendAsync(confirm2Req);
        Assert.Equal(HttpStatusCode.OK, confirm2.StatusCode);
        var c2 = await ReadJson(confirm2);
        Assert.Equal("CONFIRMED", c2.GetProperty("status").GetString());

        var classRes = await _client.GetAsync($"/classes/{classId}");
        var classJson = await ReadJson(classRes);
        Assert.Equal(1, classJson.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("heldSeats").GetInt32());
        Assert.Equal(1, classJson.GetProperty("availableSeats").GetInt32());

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        meReq.Headers.Add("X-Student-Id", studentId);
        var meRes = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        var my = await ReadJson(meRes);
        var item = my.EnumerateArray().Single(x => x.GetProperty("holdId").GetString() == holdId);
        Assert.Equal("CONFIRMED", item.GetProperty("status").GetString());
        Assert.Equal(classId, item.GetProperty("classId").GetString());
        Assert.Equal(className, item.GetProperty("className").GetString());
    }

    [Fact]
    public async Task HoldRules_ConflictOwnershipCancelAndGet()
    {
        var cls = await CreateClassAsync(totalSeats: 2);
        var classId = cls.GetProperty("id").GetString()!;
        var s1 = "20152691234567011";
        var s2 = "20152691234567012";

        var hold = await CreateHoldAsync(classId, s1);
        var holdId = hold.GetProperty("holdId").GetString()!;

        // duplicate active application in same class
        await CreateHoldAsync(classId, s1, HttpStatusCode.Conflict);

        // another student can still take remaining seat
        var hold2 = await CreateHoldAsync(classId, s2);
        var holdId2 = hold2.GetProperty("holdId").GetString()!;

        // no seats left
        await CreateHoldAsync(classId, "20152691234567013", HttpStatusCode.Conflict);

        // cannot confirm/cancel someone else's hold
        using var wrongConfirm = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        wrongConfirm.Headers.Add("X-Student-Id", s2);
        var wrongConfirmRes = await _client.SendAsync(wrongConfirm);
        Assert.Equal(HttpStatusCode.Forbidden, wrongConfirmRes.StatusCode);

        using var wrongCancel = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId2}");
        wrongCancel.Headers.Add("X-Student-Id", s1);
        var wrongCancelRes = await _client.SendAsync(wrongCancel);
        Assert.Equal(HttpStatusCode.Forbidden, wrongCancelRes.StatusCode);

        // cancel frees seat
        using var cancelReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId2}");
        cancelReq.Headers.Add("X-Student-Id", s2);
        var cancelRes = await _client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.NoContent, cancelRes.StatusCode);

        var classRes = await _client.GetAsync($"/classes/{classId}");
        var classJson = await ReadJson(classRes);
        Assert.Equal(1, classJson.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(1, classJson.GetProperty("availableSeats").GetInt32());

        // GET hold returns current status and required fields
        var getHoldRes = await _client.GetAsync($"/holds/{holdId2}");
        Assert.Equal(HttpStatusCode.OK, getHoldRes.StatusCode);
        var got = await ReadJson(getHoldRes);
        Assert.Equal(holdId2, got.GetProperty("holdId").GetString());
        Assert.Equal(classId, got.GetProperty("classId").GetString());
        Assert.Equal("CANCELLED", got.GetProperty("status").GetString());
        Assert.True(got.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task ExpiredHold_CannotBeConfirmed_And_StatusBecomesExpired()
    {
        var cls = await CreateClassAsync(totalSeats: 1);
        var classId = cls.GetProperty("id").GetString()!;
        var studentId = "20152691234567021";

        var hold = await CreateHoldAsync(classId, studentId);
        var holdId = hold.GetProperty("holdId").GetString()!;

        await Task.Delay(2500); // HOLD_DURATION_SECONDS=2 in tests

        // status should be reported as expired (read-time/background expiry)
        var getHoldRes = await _client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, getHoldRes.StatusCode);
        var got = await ReadJson(getHoldRes);
        Assert.Equal("EXPIRED", got.GetProperty("status").GetString());

        using var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirmReq.Headers.Add("X-Student-Id", studentId);
        var confirmRes = await _client.SendAsync(confirmReq);
        Assert.Equal(HttpStatusCode.Conflict, confirmRes.StatusCode);

        var classRes = await _client.GetAsync($"/classes/{classId}");
        var classJson = await ReadJson(classRes);
        Assert.Equal(0, classJson.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(1, classJson.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task ConcurrentHolds_DoNotExceedSeatLimit()
    {
        var cls = await CreateClassAsync(totalSeats: 10);
        var classId = cls.GetProperty("id").GetString()!;

        var tasks = Enumerable.Range(1, 40).Select(async i =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", StudentId(i));
            return await _client.SendAsync(req);
        });

        var responses = await Task.WhenAll(tasks);
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var other = responses.Count(r => r.StatusCode != HttpStatusCode.Created && r.StatusCode != HttpStatusCode.Conflict);
        Assert.Equal(0, other);
        Assert.Equal(10, created);
        Assert.Equal(30, conflicts);

        var classRes = await _client.GetAsync($"/classes/{classId}");
        var classJson = await ReadJson(classRes);
        Assert.Equal(10, classJson.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("availableSeats").GetInt32());
        Assert.True(classJson.GetProperty("heldSeats").GetInt32() + classJson.GetProperty("confirmedSeats").GetInt32() <= classJson.GetProperty("totalSeats").GetInt32());
    }
}
