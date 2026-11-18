using VietStay.Api.Domain;

namespace VietStay.Api.Contracts;

// ---------- Danh mục ----------

public sealed record CityDto(string Id, string Name, string Image);

public sealed record HotelSummaryDto(
    string Id, string Name, string CityId, string City, string Address, int Stars, double Rating, int ReviewCount,
    string Image, IReadOnlyList<string> Amenities,
    // Giá/đêm thấp nhất (trong các phòng còn trống & đủ chỗ khi tìm kiếm).
    long MinPrice);

public sealed record RoomDto(
    string Id, string Name, string Description, int Size, string Beds, int MaxGuests, long Price, string Image,
    bool FreeCancellation, bool BreakfastIncluded);

public sealed record ReviewDto(string Id, string Author, double Rating, string Comment, DateOnly Date);

public sealed record HotelDetailDto(
    string Id, string Name, string CityId, string City, string Address, int Stars, double Rating, int ReviewCount,
    string Description, IReadOnlyList<string> Images, IReadOnlyList<string> Amenities,
    IReadOnlyList<RoomDto> Rooms, IReadOnlyList<ReviewDto> Reviews);

public sealed record StayDto(DateOnly CheckIn, DateOnly CheckOut, int Nights, int Guests, int Rooms);

public sealed record SearchResultDto(
    IReadOnlyList<HotelSummaryDto> Items, int Total, int Page, int PageSize, StayDto Stay);

public sealed record RoomOfferDto(RoomDto Room, int Remaining, bool FitsGuests, bool Bookable, long TotalBeforeTax);

/// <summary>Query string chung cho ngày ở & số khách. Ngày định dạng yyyy-MM-dd.</summary>
public sealed record StayQuery(string? CheckIn, string? CheckOut, int? Guests, int? Rooms);

public sealed record SearchQuery(
    string? Q, string? CheckIn, string? CheckOut, int? Guests, int? Rooms,
    long? MaxPrice, int? MinStars, double? MinRating,
    // Danh sách tiện nghi phân tách bằng dấu phẩy, vd. "wifi,pool".
    string? Amenities,
    // recommended | price-asc | price-desc | rating
    string? Sort, int? Page, int? PageSize);

/// <summary>Gợi ý khi gõ ô tìm kiếm. radiusKm: chỉ lấy khách sạn trong bán kính này quanh địa điểm.</summary>
public sealed record SuggestQuery(
    string? Q, string? CheckIn, string? CheckOut, int? Guests, int? Rooms, int? Limit, double? RadiusKm);

/// <summary>Địa điểm hiểu được từ từ khóa: "city" (trung tâm thành phố) hoặc "hotel" (vị trí khách sạn).</summary>
public sealed record PlaceDto(string Type, string Id, string Name, double Lat, double Lng);

public sealed record HotelSuggestionDto(HotelSummaryDto Hotel, double DistanceKm);

/// <summary>Place = null khi không nhận ra địa điểm nào. Items sắp theo giá thấp → cao.</summary>
public sealed record SuggestResultDto(PlaceDto? Place, IReadOnlyList<HotelSuggestionDto> Items, double RadiusKm);

// ---------- Tài khoản ----------

public sealed record RegisterRequest(string? Name, string? Email, string? Phone, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserDto(string Id, string Name, string Email, string Phone);

public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public sealed record UpdateProfileRequest(string? Name, string? Phone);

// ---------- Đặt phòng ----------

public sealed record CreateBookingRequest(
    string? HotelId, string? RoomId, DateOnly? CheckIn, DateOnly? CheckOut, int? Guests, int? Rooms,
    string? GuestName, string? GuestEmail, string? GuestPhone, string? Note, PaymentMethod? PaymentMethod);

public sealed record BookingDto(
    string Id, string Code, string HotelId, string HotelName, string HotelImage, string City, string Address,
    string RoomId, string RoomName, DateOnly CheckIn, DateOnly CheckOut, int Nights, int Guests, int Rooms,
    long PricePerNight, long Subtotal, long Taxes, long Total,
    string GuestName, string GuestEmail, string GuestPhone, string? Note,
    PaymentMethod PaymentMethod, BookingStatus Status, bool CanCancel, DateTime CreatedAt, DateTime? CancelledAt);

public static class Mapping
{
    public static CityDto ToDto(this City c) => new(c.Id, c.Name, c.Image);

    public static RoomDto ToDto(this Room r) =>
        new(r.Id, r.Name, r.Description, r.Size, r.Beds, r.MaxGuests, r.Price, r.Image, r.FreeCancellation, r.BreakfastIncluded);

    public static HotelSummaryDto ToSummary(this Hotel h, long minPrice) =>
        new(h.Id, h.Name, h.CityId, h.City, h.Address, h.Stars, h.Rating, h.ReviewCount,
            h.Images.FirstOrDefault() ?? "", h.Amenities, minPrice);

    public static HotelDetailDto ToDetail(this Hotel h) =>
        new(h.Id, h.Name, h.CityId, h.City, h.Address, h.Stars, h.Rating, h.ReviewCount, h.Description,
            h.Images, h.Amenities, h.Rooms.Select(ToDto).ToList(),
            h.Reviews.Select(r => new ReviewDto(r.Id, r.Author, r.Rating, r.Comment, r.Date)).ToList());

    public static UserDto ToDto(this User u) => new(u.Id, u.Name, u.Email, u.Phone);

    public static BookingDto ToDto(this Booking b, DateOnly today) =>
        new(b.Id, b.Code, b.HotelId, b.HotelName, b.HotelImage, b.City, b.Address, b.RoomId, b.RoomName,
            b.CheckIn, b.CheckOut, b.Nights, b.Guests, b.Rooms, b.PricePerNight, b.Subtotal, b.Taxes, b.Total,
            b.GuestName, b.GuestEmail, b.GuestPhone, b.Note, b.PaymentMethod, b.Status,
            b.Status == BookingStatus.Confirmed && b.CheckIn > today, b.CreatedAt, b.CancelledAt);
}
