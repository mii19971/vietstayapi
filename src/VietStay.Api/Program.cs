using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using Scalar.AspNetCore;
using VietStay.Api;
using VietStay.Api.Data;
using VietStay.Api.Domain;
using VietStay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Cấu hình ----------
builder.Services.AddOptions<MongoOptions>().BindConfiguration(MongoOptions.Section);
builder.Services.AddOptions<HbeOptions>().BindConfiguration(HbeOptions.Section);
builder.Services.AddOptions<BookingOptions>().BindConfiguration(BookingOptions.Section);
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section)
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey phải dài tối thiểu 32 byte.")
    .ValidateOnStart();

// ---------- MongoDB ----------
MongoSetup.ConfigureSerialization();
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
builder.Services.AddSingleton<MongoDb>();
builder.Services.AddSingleton<HbeDb>(); // nội dung khách sạn — chỉ đọc
builder.Services.AddHostedService<MongoInitializer>();

// ---------- Dịch vụ nghiệp vụ ----------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddHostedService<CatalogWarmUp>();
builder.Services.AddScoped<StayService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AccountService>();

// ---------- JSON: camelCase, enum dạng snake_case ("pay_at_hotel", "confirmed") ----------
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));

// ---------- Xác thực JWT ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false; // giữ nguyên claim "sub"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// ---------- Hạ tầng HTTP ----------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongo");
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
var allowAnyLocalhost = builder.Environment.IsDevelopment();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin =>
        corsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        // Development: chấp nhận mọi cổng trên localhost (Expo tự đổi sang 8082, 8083... khi 8081 bận).
        || (allowAnyLocalhost && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
     .AllowAnyHeader()
     .AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Chống dò mật khẩu: giới hạn số request đăng nhập/đăng ký mỗi phút cho mỗi IP (mặc định 10).
    var authPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
    o.AddPolicy(Endpoints.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("VietStay API"));
}

app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/scalar" : "/health")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapVietStayApi();

app.Run();

/// <summary>Kiểm tra kết nối MongoDB cho /health.</summary>
internal sealed class MongoHealthCheck(MongoDb db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được MongoDB", ex);
        }
    }
}
