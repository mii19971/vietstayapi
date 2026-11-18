using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace VietStay.Api.Domain;

public static class Geo
{
    /// <summary>Điểm GeoJSON (MongoDB lưu theo thứ tự kinh độ, vĩ độ).</summary>
    public static GeoJsonPoint<GeoJson2DGeographicCoordinates> Point(double lat, double lng) =>
        new(new GeoJson2DGeographicCoordinates(lng, lat));

    /// <summary>Khoảng cách đường chim bay (km) theo công thức haversine.</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }
}

public static class Amenities
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "wifi", "pool", "spa", "gym", "restaurant", "parking", "breakfast", "airport", "beach", "ac", "pet",
    };
}

public sealed class City
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>Tọa độ trung tâm thành phố.</summary>
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public sealed class Hotel
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CityId { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public int Stars { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public string Description { get; set; } = "";
    public List<string> Images { get; set; } = [];
    public List<string> Amenities { get; set; } = [];
    public List<Room> Rooms { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    /// <summary>Tên + thành phố + địa chỉ đã bỏ dấu, chữ thường — phục vụ tìm kiếm không dấu.</summary>
    public string SearchText { get; set; } = "";
    /// <summary>Vị trí khách sạn (index 2dsphere) — dùng cho gợi ý "gần nhất".</summary>
    public GeoJsonPoint<GeoJson2DGeographicCoordinates>? Location { get; set; }
}

public sealed class Room
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Size { get; set; }
    public string Beds { get; set; } = "";
    /// <summary>Số khách tối đa mỗi phòng.</summary>
    public int MaxGuests { get; set; }
    /// <summary>Giá VND mỗi đêm mỗi phòng.</summary>
    public long Price { get; set; }
    public string Image { get; set; } = "";
    /// <summary>Tổng số phòng của loại này.</summary>
    public int Inventory { get; set; }
    public bool FreeCancellation { get; set; }
    public bool BreakfastIncluded { get; set; }
}

public sealed class Review
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public double Rating { get; set; }
    public string Comment { get; set; } = "";
    public DateOnly Date { get; set; }
}

/// <summary>Số phòng đã bán của một loại phòng trong một đêm. _id = "{roomId}:{yyyy-MM-dd}".</summary>
public sealed class RoomNight
{
    [BsonId] public string Id { get; set; } = "";
    public string HotelId { get; set; } = "";
    public string RoomId { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Booked { get; set; }

    public static string Key(string roomId, DateOnly date) => $"{roomId}:{date:yyyy-MM-dd}";
}

public enum BookingStatus { Confirmed, Cancelled }

public enum PaymentMethod { PayAtHotel, BankTransfer, Card }

public sealed class Booking
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Code { get; set; } = "";
    public string UserId { get; set; } = "";
    public string HotelId { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string HotelImage { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string RoomName { get; set; } = "";
    public bool FreeCancellation { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Nights { get; set; }
    public int Guests { get; set; }
    public int Rooms { get; set; }
    public long PricePerNight { get; set; }
    public long Subtotal { get; set; }
    public long Taxes { get; set; }
    public long Total { get; set; }
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
    public List<string> Favorites { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
