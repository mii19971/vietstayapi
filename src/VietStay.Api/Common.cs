using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace VietStay.Api;

/// <summary>Lỗi nghiệp vụ, được trả về dưới dạng ProblemDetails.</summary>
public sealed class ApiException(int status, string title, IDictionary<string, string[]>? errors = null) : Exception(title)
{
    public int Status { get; } = status;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException NotFound(string title) => new(StatusCodes.Status404NotFound, title);
    public static ApiException Conflict(string title) => new(StatusCodes.Status409Conflict, title);
}

/// <summary>Gom lỗi kiểm tra dữ liệu đầu vào rồi ném một lần (HTTP 400).</summary>
public sealed class Validation
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public Validation Check(bool ok, string field, string message)
    {
        if (!ok)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }
        return this;
    }

    public bool Has(string field) => _errors.ContainsKey(field);

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "Dữ liệu không hợp lệ",
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApiException ex) return false;
        http.Response.StatusCode = ex.Status;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = ex.Status, Title = ex.Message };
        if (ex.Errors is not null) problem.Extensions["errors"] = ex.Errors;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}

public static partial class Text
{
    /// <summary>Bỏ dấu tiếng Việt + chữ thường: "Đà Nẵng" → "da nang".</summary>
    public static string Normalize(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant().Trim();
    }

    public static bool IsEmail(string s) => EmailRegex().IsMatch(s.Trim());

    public static bool IsPhone(string s) => PhoneRegex().IsMatch(CleanPhone(s));

    public static string CleanPhone(string s) => Regex.Replace(s, @"[\s.\-]", "");

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(0|\+84)\d{9,10}$")]
    private static partial Regex PhoneRegex();
}

/// <summary>Đồng hồ theo múi giờ kinh doanh (mặc định giờ Việt Nam).</summary>
public sealed class AppClock(TimeProvider time, IOptions<BookingOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);
}

public static class UserExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Chưa đăng nhập");
}
