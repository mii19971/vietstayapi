namespace VietStay.Api;

/// <summary>Database của VietStay (tài khoản, yêu thích, đơn đặt phòng) — đọc/ghi.</summary>
public sealed class MongoOptions
{
    public const string Section = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "vietstay";
}

/// <summary>
/// Database nội dung khách sạn "hbe" — chỉ đọc. Dùng các collection: country, en_eap_properties,
/// vi_eap_properties, epa_regions, places, references. API không tạo index hay ghi gì vào database này.
/// </summary>
public sealed class HbeOptions
{
    public const string Section = "Hbe";
    /// <summary>Để trống = dùng chung Mongo:ConnectionString.</summary>
    public string? ConnectionString { get; set; }
    public string Database { get; set; } = "hbe";
    /// <summary>Thời gian tối đa (ms) cho một truy vấn full-text trên collection places.</summary>
    public int SuggestTimeoutMs { get; set; } = 4000;
    /// <summary>Thời gian giữ danh sách khách sạn của một vùng trong bộ nhớ.</summary>
    public int CacheMinutes { get; set; } = 30;
    /// <summary>Điểm đến phổ biến ở trang chủ (region_id trong epa_regions) + ảnh đại diện.</summary>
    public List<DestinationOption> Destinations { get; set; } = [];
    /// <summary>Số điểm đến đầu tiên dùng để chọn "khách sạn nổi bật".</summary>
    public int FeaturedFromDestinations { get; set; } = 4;
    /// <summary>Khách sạn nổi bật phải có ít nhất chừng này lượt đánh giá.</summary>
    public int FeaturedMinReviews { get; set; } = 100;
    /// <summary>Nạp sẵn danh sách khách sạn của các điểm đến phổ biến khi khởi động (chạy nền).</summary>
    public bool WarmUp { get; set; } = true;
}

public sealed class DestinationOption
{
    public string RegionId { get; set; } = "";
    public string Image { get; set; } = "";
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
    public int MaxNights { get; set; } = 30;
    public int MaxGuests { get; set; } = 20;
    public int MaxRooms { get; set; } = 8;
    /// <summary>Múi giờ dùng để xác định "hôm nay" (ngày nhận phòng không được ở quá khứ).</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
}
