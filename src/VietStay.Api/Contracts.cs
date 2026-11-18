using VietStay.Api.Domain;

namespace VietStay.Api.Contracts;

// ---------- Danh mục (đọc từ database hbe) ----------
// Mọi endpoint danh mục nhận ?lang=vi|en (mặc định vi). Id khách sạn = external_property_id, id vùng = region_id.

public sealed record CountryDto(string Code, string Name, string RegionId);

/// <summary>Điểm đến phổ biến (một vùng trong epa_regions). Id = region_id.</summary>
public sealed record DestinationDto(string Id, string Name, string Image, int HotelCount);

/// <summary>Gợi ý khi gõ. Type = "region" (Id = region_id) hoặc "hotel" (Id = external_property_id).</summary>
public sealed record PlaceSuggestionDto(
    string Type, string Id, string Name, string? Subtitle, string? RegionType, string? CountryCode);

public sealed record RegionDto(
    string Id, string Name, string? Type, string? Subtitle, string? CountryCode, string? ParentId,
    double? Lat, double? Lng, int HotelCount);

/// <summary>
/// Khách sạn dạng rút gọn. Stars = hạng sao (0 = chưa xếp hạng, có thể lẻ .5).
/// Rating = điểm khách đánh giá quy về thang 10 (0 = chưa có đánh giá).
/// Amenities = mã tiện nghi chính (xem <see cref="Services.AmenityMap"/>).
/// </summary>
public sealed record HotelSummaryDto(
    string Id, string Name, string? Category, string City, string Address, string? CountryCode,
    double Stars, double Rating, int ReviewCount, string Image, IReadOnlyList<string> Amenities,
    double? Lat, double? Lng);

public sealed record SearchResultDto(
    IReadOnlyList<HotelSummaryDto> Items, int Total, int Page, int PageSize,
    // Vùng đang liệt kê; null khi từ khóa chỉ khớp tên khách sạn.
    RegionDto? Region);

/// <summary>
/// Tìm khách sạn theo vùng (regionId) hoặc từ khóa (q — tự nhận diện vùng phù hợp nhất, không có thì khớp tên khách sạn).
/// amenities: mã phân tách bằng dấu phẩy, vd. "wifi,pool". sort: recommended | rating | stars | name.
/// </summary>
public sealed record SearchQuery(
    string? Q, string? RegionId, double? MinStars, double? MinRating, string? Amenities,
    string? Sort, int? Page, int? PageSize, string? Lang);

public sealed record SuggestQuery(string? Q, int? Limit, string? Lang);

public sealed record ImageDto(string Url, string Thumb, string? Caption);

/// <summary>Mục có tên (tiện nghi, thuộc tính, thống kê). Name đã ghép giá trị nếu có (vd. "Số lượng nhà hàng: 2").</summary>
public sealed record NamedItemDto(string Id, string Name, string? Value);

/// <summary>Điểm thành phần, thang 10.</summary>
public sealed record RatingBreakdownDto(double? Cleanliness, double? Service, double? Comfort, double? Condition, double? Amenities);

public sealed record RoomDto(
    string Id, string Name, string Description, double? SizeM2, IReadOnlyList<string> Beds,
    int? MaxGuests, int? MaxAdults, int? MaxChildren,
    IReadOnlyList<ImageDto> Images, IReadOnlyList<string> Amenities, IReadOnlyList<string> Views);

/// <summary>Đoạn mô tả (văn bản thuần, đã bỏ HTML).</summary>
public sealed record DescriptionSectionDto(string Key, string Title, string Text);

public sealed record PoliciesDto(
    string? CheckInFrom, string? CheckInUntil, string? CheckOutBefore,
    string? CheckInInstructions, string? SpecialInstructions, string? KnowBeforeYouGo,
    string? MandatoryFees, string? OptionalFees,
    IReadOnlyList<string> Pets, IReadOnlyList<string> General);

public sealed record HotelDetailDto(
    string Id, string Name, string? Category, string City, string Address, string? CountryCode,
    double Stars, double Rating, int ReviewCount, RatingBreakdownDto? RatingBreakdown,
    string? Headline, string Description, IReadOnlyList<DescriptionSectionDto> Sections,
    IReadOnlyList<ImageDto> Images, IReadOnlyList<string> AmenityKeys, IReadOnlyList<NamedItemDto> Amenities,
    IReadOnlyList<RoomDto> Rooms, PoliciesDto Policies, IReadOnlyList<NamedItemDto> Statistics,
    string? Phone, double? Lat, double? Lng);

/// <summary>Một dòng trong bảng tra cứu references. Id = epa_id.</summary>
public sealed record ReferenceDto(string Id, string Name, string? Group);

// ---------- Tài khoản ----------

public sealed record RegisterRequest(string? Name, string? Email, string? Phone, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserDto(string Id, string Name, string Email, string Phone);

public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public sealed record UpdateProfileRequest(string? Name, string? Phone);

// ---------- Đặt phòng ----------

/// <summary>HotelId = external_property_id, RoomId = id phòng trong khách sạn (hbe).</summary>
public sealed record CreateBookingRequest(
    string? HotelId, string? RoomId, DateOnly? CheckIn, DateOnly? CheckOut, int? Guests, int? Rooms,
    string? GuestName, string? GuestEmail, string? GuestPhone, string? Note, PaymentMethod? PaymentMethod);

/// <summary>Các trường giá = null khi chưa có giá (hbe không chứa giá; khách sạn xác nhận sau).</summary>
public sealed record BookingDto(
    string Id, string Code, string HotelId, string HotelName, string HotelImage, string City, string Address,
    string RoomId, string RoomName, DateOnly CheckIn, DateOnly CheckOut, int Nights, int Guests, int Rooms,
    long? PricePerNight, long? Subtotal, long? Taxes, long? Total,
    string GuestName, string GuestEmail, string GuestPhone, string? Note,
    PaymentMethod PaymentMethod, BookingStatus Status, bool CanCancel, DateTime CreatedAt, DateTime? CancelledAt);

public static class Mapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Name, u.Email, u.Phone);

    public static BookingDto ToDto(this Booking b, DateOnly today) =>
        new(b.Id, b.Code, b.HotelId, b.HotelName, b.HotelImage, b.City, b.Address, b.RoomId, b.RoomName,
            b.CheckIn, b.CheckOut, b.Nights, b.Guests, b.Rooms, b.PricePerNight, b.Subtotal, b.Taxes, b.Total,
            b.GuestName, b.GuestEmail, b.GuestPhone, b.Note, b.PaymentMethod, b.Status,
            b.Status != BookingStatus.Cancelled && b.CheckIn > today, b.CreatedAt, b.CancelledAt);
}
