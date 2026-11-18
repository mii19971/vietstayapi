using System.Security.Claims;
using VietStay.Api.Contracts;
using VietStay.Api.Services;

namespace VietStay.Api;

public static class Endpoints
{
    public const string AuthRateLimit = "auth";

    public static void MapVietStayApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---------- Danh mục (công khai, đọc từ database hbe; ?lang=vi|en, mặc định vi) ----------
        var catalog = api.MapGroup("").WithTags("Catalog");

        catalog.MapGet("/countries", (CatalogService s, CancellationToken ct, string? lang) =>
                s.GetCountriesAsync(Langs.Parse(lang), ct))
            .WithSummary("Danh sách quốc gia (collection country)");

        catalog.MapGet("/destinations", (CatalogService s, CancellationToken ct, string? lang) =>
                s.GetDestinationsAsync(Langs.Parse(lang), ct))
            .WithSummary("Điểm đến phổ biến")
            .WithDescription("Các vùng cấu hình ở Hbe:Destinations; id = region_id, dùng cho /api/hotels/search?regionId=.");

        catalog.MapGet("/places/suggest", ([AsParameters] SuggestQuery q, CatalogService s, CancellationToken ct) => s.SuggestAsync(q, ct))
            .WithSummary("Gợi ý khi gõ ô tìm kiếm (collection places)")
            .WithDescription("Khớp mọi từ (không dấu cũng được, theo từ hoàn chỉnh) với tên vùng/khách sạn. Kết quả: type = region (id = region_id) hoặc hotel (id = id khách sạn). Ưu tiên Việt Nam, vùng trước khách sạn.");

        catalog.MapGet("/regions/{id}", (string id, CatalogService s, CancellationToken ct, string? lang) =>
                s.GetRegionAsync(id, Langs.Parse(lang), ct))
            .WithSummary("Thông tin vùng (collection epa_regions)");

        catalog.MapGet("/hotels/search", ([AsParameters] SearchQuery q, CatalogService s, CancellationToken ct) => s.SearchAsync(q, ct))
            .WithSummary("Tìm khách sạn theo vùng hoặc từ khóa")
            .WithDescription("Cần `regionId` hoặc `q`. Lọc `minStars` (0–5), `minRating` (thang 10), `amenities` (wifi,pool,spa,gym,restaurant,parking,breakfast,airport,beach,pet); `sort` = recommended | rating | stars | name.");

        catalog.MapGet("/hotels/featured", (CatalogService s, CancellationToken ct, int? limit, string? lang) =>
                s.GetFeaturedAsync(limit ?? 6, Langs.Parse(lang), ct))
            .WithSummary("Khách sạn được đánh giá cao ở các điểm đến phổ biến");

        catalog.MapGet("/hotels/{id}", (string id, CatalogService s, CancellationToken ct, string? lang) =>
                s.GetDetailAsync(id, Langs.Parse(lang), ct))
            .WithSummary("Chi tiết khách sạn (collection vi_eap_properties / en_eap_properties)");

        catalog.MapGet("/references", (CatalogService s, CancellationToken ct, string? type, string? ids, string? lang) =>
                s.GetReferencesAsync(type, ids, Langs.Parse(lang), ct))
            .WithSummary("Bảng tra cứu (collection references)")
            .WithDescription("`type` bắt buộc, vd. amenities_property, amenities_rooms, attributes_pets, categories, room_views, bed_groups. `ids` = danh sách epa_id phân tách bằng dấu phẩy (tùy chọn).");

        // ---------- Tài khoản ----------
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(AuthRateLimit);

        auth.MapPost("/register", async (RegisterRequest req, AccountService s, CancellationToken ct) =>
                TypedResults.Created("/api/me", await s.RegisterAsync(req, ct)))
            .WithSummary("Đăng ký");

        auth.MapPost("/login", (LoginRequest req, AccountService s, CancellationToken ct) => s.LoginAsync(req, ct))
            .WithSummary("Đăng nhập, trả về JWT");

        var me = api.MapGroup("/me").WithTags("Me").RequireAuthorization();

        me.MapGet("", (ClaimsPrincipal u, AccountService s, CancellationToken ct) => s.GetProfileAsync(u.UserId(), ct))
            .WithSummary("Thông tin tài khoản");

        me.MapPut("", (UpdateProfileRequest req, ClaimsPrincipal u, AccountService s, CancellationToken ct) =>
                s.UpdateProfileAsync(u.UserId(), req, ct))
            .WithSummary("Cập nhật họ tên / số điện thoại");

        me.MapGet("/favorites", (ClaimsPrincipal u, AccountService s, CancellationToken ct, string? lang) =>
                s.GetFavoritesAsync(u.UserId(), Langs.Parse(lang), ct))
            .WithSummary("Khách sạn yêu thích");

        me.MapPut("/favorites/{hotelId}", async (string hotelId, ClaimsPrincipal u, AccountService s, CancellationToken ct) =>
            {
                await s.AddFavoriteAsync(u.UserId(), hotelId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Thêm vào yêu thích");

        me.MapDelete("/favorites/{hotelId}", async (string hotelId, ClaimsPrincipal u, AccountService s, CancellationToken ct) =>
            {
                await s.RemoveFavoriteAsync(u.UserId(), hotelId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Bỏ khỏi yêu thích");

        // ---------- Đặt phòng ----------
        var bookings = api.MapGroup("/bookings").WithTags("Bookings").RequireAuthorization();

        bookings.MapPost("", async (CreateBookingRequest req, ClaimsPrincipal u, BookingService s, CancellationToken ct) =>
            {
                var booking = await s.CreateAsync(u.UserId(), req, ct);
                return TypedResults.Created($"/api/bookings/{booking.Id}", booking);
            })
            .WithSummary("Gửi yêu cầu đặt phòng (trạng thái pending, chưa có giá)");

        bookings.MapGet("", (ClaimsPrincipal u, BookingService s, CancellationToken ct) => s.ListAsync(u.UserId(), ct))
            .WithSummary("Đơn đặt phòng của tôi");

        bookings.MapGet("/{id}", (string id, ClaimsPrincipal u, BookingService s, CancellationToken ct) => s.GetAsync(u.UserId(), id, ct))
            .WithSummary("Chi tiết đơn");

        bookings.MapPost("/{id}/cancel", (string id, ClaimsPrincipal u, BookingService s, CancellationToken ct) =>
                s.CancelAsync(u.UserId(), id, ct))
            .WithSummary("Hủy đơn (trước ngày nhận phòng)");
    }
}
