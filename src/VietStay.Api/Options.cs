namespace VietStay.Api;

public sealed class MongoOptions
{
    public const string Section = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "vietstay";
    /// <summary>Nạp dữ liệu mẫu (thành phố, khách sạn) khi khởi động.</summary>
    public bool Seed { get; set; }
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "vietstay-api";
    public string Audience { get; set; } = "vietstay-app";
    /// <summary>Khóa ký HMAC-SHA256, tối thiểu 32 ký tự. Không commit khóa production.</summary>
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60 * 24 * 7;
}

public sealed class BookingOptions
{
    public const string Section = "Booking";
    public decimal TaxRate { get; set; } = 0.10m;
    public int MaxNights { get; set; } = 30;
    public int MaxGuests { get; set; } = 20;
    public int MaxRooms { get; set; } = 8;
    /// <summary>Múi giờ dùng để xác định "hôm nay" (ngày nhận phòng không được ở quá khứ).</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
}
