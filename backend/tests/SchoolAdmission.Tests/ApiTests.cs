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
            ?? "Host=localhost;Port=5432;Database=school_admission_test;Username=postgres;Password=postgres";

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

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CreateAndListClasses_ReturnsExpectedSeatCounts()
    {
        var uniqueName = $"Class {Guid.NewGuid():N}";
        var create = await _client.PostAsJsonAsync("/classes", new { name = uniqueName, totalSeats = 5 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal(uniqueName, created.GetProperty("name").GetString());
        Assert.Equal(5, created.GetProperty("totalSeats").GetInt32());
        var classId = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(classId));

        var list = await _client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = await ReadJson(list);
        var match = items.EnumerateArray().First(x => x.GetProperty("id").GetString() == classId);
        Assert.Equal(5, match.GetProperty("totalSeats").GetInt32());
        Assert.Equal(0, match.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, match.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, match.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task ConfirmHold_IsIdempotent_ForSameApplication()
    {
        var createClass = await _client.PostAsJsonAsync("/classes", new { name = $"Idempotent {Guid.NewGuid():N}", totalSeats = 2 });
        var cls = await ReadJson(createClass);
        var classId = cls.GetProperty("id").GetString();

        using var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", "20152691234567001");
        var holdRes = await _client.SendAsync(holdReq);
        Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);
        var hold = await ReadJson(holdRes);
        var holdId = hold.GetProperty("holdId").GetString();

        using var confirm1Req = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirm1Req.Headers.Add("X-Student-Id", "20152691234567001");
        var confirm1 = await _client.SendAsync(confirm1Req);
        Assert.Equal(HttpStatusCode.OK, confirm1.StatusCode);
        var c1 = await ReadJson(confirm1);
        Assert.Equal("CONFIRMED", c1.GetProperty("status").GetString());

        using var confirm2Req = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirm2Req.Headers.Add("X-Student-Id", "20152691234567001");
        var confirm2 = await _client.SendAsync(confirm2Req);
        Assert.Equal(HttpStatusCode.OK, confirm2.StatusCode);
        var c2 = await ReadJson(confirm2);
        Assert.Equal("CONFIRMED", c2.GetProperty("status").GetString());

        var classRes = await _client.GetAsync($"/classes/{classId}");
        var classJson = await ReadJson(classRes);
        Assert.Equal(1, classJson.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(0, classJson.GetProperty("heldSeats").GetInt32());
        Assert.Equal(1, classJson.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task ConcurrentHolds_DoNotExceedSeatLimit()
    {
        var createClass = await _client.PostAsJsonAsync("/classes", new { name = $"Concurrent {Guid.NewGuid():N}", totalSeats = 10 });
        var cls = await ReadJson(createClass);
        var classId = cls.GetProperty("id").GetString();

        var tasks = Enumerable.Range(1, 40).Select(async i =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", $"S{i:D17}");
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
