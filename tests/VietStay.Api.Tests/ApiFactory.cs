using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EphemeralMongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VietStay.Api.Contracts;

namespace VietStay.Api.Tests;

/// <summary>Chạy API thật trên một MongoDB tạm (EphemeralMongo tự tải và khởi chạy mongod).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IMongoRunner? _mongo;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task InitializeAsync()
    {
        _mongo = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            // MongoDB 8.x mới nhất cần Windows mới hơn Windows 10 (mongod thoát với 0xC0000139); 7.0 chạy được ở mọi nơi.
            Version = MongoVersion.V7,
            AdditionalArguments = ["--quiet"],
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Mongo:ConnectionString", _mongo!.ConnectionString);
        builder.UseSetting("Mongo:Database", "vietstay_test");
        builder.UseSetting("Mongo:Seed", "true");
        // Mọi test dùng chung một "IP", nên nới giới hạn đăng ký/đăng nhập.
        builder.UseSetting("RateLimit:AuthPerMinute", "1000");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        _mongo?.Dispose();
    }

    /// <summary>Đăng ký một user mới và trả về HttpClient đã gắn token.</summary>
    public async Task<HttpClient> CreateUserClientAsync()
    {
        var client = CreateClient();
        var email = $"user{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Nguyễn Văn Test", email, "0912345678", "matkhau123"), Json);
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
