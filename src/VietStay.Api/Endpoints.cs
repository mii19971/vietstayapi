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

        // ---------- Danh mục (công khai) ----------
        var catalog = api.MapGroup("").WithTags("Catalog");

        catalog.MapGet("/cities", (HotelService s, CancellationToken ct) => s.GetCitiesAsync(ct))
            .WithSummary("Danh sách điểm đến");

        catalog.MapGet("/hotels/featured", (HotelService s, CancellationToken ct, int limit = 6) => s.GetFeaturedAsync(limit, ct))
            .WithSummary("Khách sạn được đánh giá cao nhất");

        catalog.MapGet("/hotels/search", ([AsParameters] SearchQuery q, HotelService s, CancellationToken ct) => s.SearchAsync(q, ct))
            .WithSummary("Tìm khách sạn còn phòng")
            .WithDescription("Tìm không dấu theo tên/thành phố/địa chỉ, lọc theo giá, sao, điểm, tiện nghi; chỉ trả về khách sạn còn phòng phù hợp số khách cho khoảng ngày.");

        catalog.MapGet("/hotels/suggest", ([AsParameters] SuggestQuery q, HotelService s, CancellationToken ct) => s.SuggestAsync(q, ct))
            .WithSummary("Gợi ý khi gõ ô tìm kiếm")
            .WithDescription("Nhận diện địa điểm từ từ khóa (thành phố hoặc tên khách sạn, không dấu cũng được), trả về tối đa `limit` (mặc định 20) khách sạn gần nhất còn phòng trong bán kính `radiusKm` (mặc định 20 km), sắp xếp theo giá thấp → cao.");

        catalog.MapGet("/hotels/{id}", (string id, HotelService s, CancellationToken ct) => s.GetDetailAsync(id, ct))
            .WithSummary("Chi tiết khách sạn");

        catalog.MapGet("/hotels/{id}/rooms", (string id, [AsParameters] StayQuery q, HotelService s, CancellationToken ct) =>
                s.GetRoomOffersAsync(id, q, ct))
            .WithSummary("Phòng trống và giá theo khoảng ngày");

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

        me.MapGet("/favorites", (ClaimsPrincipal u, AccountService s, CancellationToken ct) => s.GetFavoritesAsync(u.UserId(), ct))
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
            .WithSummary("Đặt phòng");

        bookings.MapGet("", (ClaimsPrincipal u, BookingService s, CancellationToken ct) => s.ListAsync(u.UserId(), ct))
            .WithSummary("Đơn đặt phòng của tôi");

        bookings.MapGet("/{id}", (string id, ClaimsPrincipal u, BookingService s, CancellationToken ct) => s.GetAsync(u.UserId(), id, ct))
            .WithSummary("Chi tiết đơn");

        bookings.MapPost("/{id}/cancel", (string id, ClaimsPrincipal u, BookingService s, CancellationToken ct) =>
                s.CancelAsync(u.UserId(), id, ct))
            .WithSummary("Hủy đơn (trước ngày nhận phòng)");
    }
}
