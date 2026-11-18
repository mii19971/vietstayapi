using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VietStay.Api.Domain;

// Document của database VietStay (đọc/ghi). Nội dung khách sạn nằm ở database hbe — xem Hbe.cs.

/// <summary>
/// Pending = yêu cầu đặt phòng đã gửi, chờ khách sạn xác nhận (dữ liệu hbe không có giá/tồn phòng).
/// Confirmed = đơn đã xác nhận (đơn cũ trước khi chuyển sang hbe).
/// </summary>
public enum BookingStatus { Pending, Confirmed, Cancelled }

public enum PaymentMethod { PayAtHotel, BankTransfer, Card }

public sealed class Booking
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Code { get; set; } = "";
    public string UserId { get; set; } = "";
    /// <summary>external_property_id trong hbe.</summary>
    public string HotelId { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string HotelImage { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string RoomName { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Nights { get; set; }
    public int Guests { get; set; }
    public int Rooms { get; set; }
    /// <summary>Giá (VND). null khi chưa có giá — hbe không chứa giá phòng.</summary>
    public long? PricePerNight { get; set; }
    public long? Subtotal { get; set; }
    public long? Taxes { get; set; }
    public long? Total { get; set; }
    public string GuestName { get; set; } = "";
    public string GuestEmail { get; set; } = "";
    public string GuestPhone { get; set; } = "";
    public string? Note { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public sealed class User
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    /// <summary>Luôn lưu chữ thường.</summary>
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>external_property_id của khách sạn (hbe).</summary>
    public List<string> Favorites { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
