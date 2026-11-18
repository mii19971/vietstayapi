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

    /// <summary>
    /// HTML trong hbe (mô tả, chính sách) → văn bản thuần cho app: &lt;br&gt;, &lt;p&gt;, &lt;li&gt; thành xuống dòng
    /// (mục danh sách có "• "), bỏ thẻ, giải mã entity, gộp khoảng trắng.
    /// </summary>
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = BreakRegex().Replace(html, "\n");
        s = ListItemRegex().Replace(s, "\n• ");
        s = BlockEndRegex().Replace(s, "\n");
        s = TagRegex().Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s).Replace(' ', ' ');
        s = SpacesRegex().Replace(s, " ");
        s = NewlinesRegex().Replace(s, "\n");
        return s.Trim().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Số lưu dạng chuỗi trong hbe ("4.5", "26.0"). Không đọc được → null.</summary>
    public static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;

    public static bool IsEmail(string s) => EmailRegex().IsMatch(s.Trim());

    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<\s*li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<\s*/\s*(p|div|ul|ol|li|h\d)\s*>|<\s*p\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex NewlinesRegex();

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

public static class Langs
{
    /// <summary>"vi" (mặc định) hoặc "en".</summary>
    public static VietStay.Api.Domain.Hbe.Lang Parse(string? lang, Validation v)
    {
        switch (lang?.Trim().ToLowerInvariant())
        {
            case null or "" or "vi": return VietStay.Api.Domain.Hbe.Lang.Vi;
            case "en": return VietStay.Api.Domain.Hbe.Lang.En;
            default:
                v.Check(false, "lang", "Ngôn ngữ hợp lệ: vi, en.");
                return VietStay.Api.Domain.Hbe.Lang.Vi;
        }
    }

    public static VietStay.Api.Domain.Hbe.Lang Parse(string? lang)
    {
        var v = new Validation();
        var result = Parse(lang, v);
        v.ThrowIfInvalid();
        return result;
    }

    /// <summary>Chọn tên theo ngôn ngữ, thiếu thì dùng tên ngôn ngữ kia.</summary>
    public static string Pick(this VietStay.Api.Domain.Hbe.Lang lang, string? vi, string? en)
    {
        // Một số tên trong hbe lưu dạng tổ hợp (NFD, vd. "Việt Nam") → chuẩn hóa NFC.
        static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Normalize(NormalizationForm.FormC);
        return (lang == VietStay.Api.Domain.Hbe.Lang.En ? N(en) ?? N(vi) : N(vi) ?? N(en)) ?? "";
    }
}

public static class UserExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Chưa đăng nhập");
}
