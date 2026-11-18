using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VietStay.Api.Domain.Hbe;

// Ánh xạ (chỉ đọc) các collection của database hbe. Tên trường giữ nguyên snake_case như trong DB;
// mọi trường đều có thể thiếu hoặc null nên khai báo nullable. Trường không khai báo sẽ bị bỏ qua.

/// <summary>Ngôn ngữ nội dung: vi → vi_eap_properties + tên vi_*, en → en_eap_properties + tên en_*.</summary>
public enum Lang { Vi, En }

/// <summary>Collection <c>country</c>.</summary>
public sealed class HbeCountry
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("country_code")] public string? CountryCode { get; set; }
    [BsonElement("en_country_name")] public string? EnName { get; set; }
    [BsonElement("vi_country_name")] public string? ViName { get; set; }
    [BsonElement("region_id")] public string? RegionId { get; set; }
}

/// <summary>
/// Collection <c>references</c>: bảng tra mã (tiện nghi, loại hình, giường, view...). <c>epa_id</c> là khóa
/// được dùng trong document khách sạn (vd. <c>amenities["2390"]</c> = Wifi miễn phí).
/// </summary>
public sealed class HbeReference
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("system_id")] public int? SystemId { get; set; }
    [BsonElement("epa_id")] public string? EpaId { get; set; }
    [BsonElement("type")] public string? Type { get; set; }
    [BsonElement("en_name")] public string? EnName { get; set; }
    [BsonElement("vi_name")] public string? ViName { get; set; }
    [BsonElement("group")] public string? Group { get; set; }
    [BsonElement("has_value")] public bool? HasValue { get; set; }
}

/// <summary>
/// Collection <c>epa_regions</c>: vùng (quốc gia, tỉnh, thành phố, khu phố, điểm tham quan, sân bay...).
/// <c>external_property_ids</c> = khách sạn nằm trong vùng; <c>external_property_expanded_ids</c> = kể cả lân cận.
/// </summary>
public sealed class HbeRegion
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("region_id")] public string RegionId { get; set; } = "";
    [BsonElement("en_region_name")] public string? EnName { get; set; }
    [BsonElement("vi_region_name")] public string? ViName { get; set; }
    [BsonElement("en_region_type")] public string? Type { get; set; }
    [BsonElement("country_code")] public string? CountryCode { get; set; }
    [BsonElement("ancestor_en_name")] public string? AncestorEnName { get; set; }
    [BsonElement("ancestor_vi_name")] public string? AncestorViName { get; set; }
    [BsonElement("current_ancestor_id")] public string? ParentId { get; set; }
    [BsonElement("ancestors")] public List<HbeAncestor>? Ancestors { get; set; }
    [BsonElement("coordinates")] public HbeRegionCoordinates? Coordinates { get; set; }
    /// <summary>Chỉ được nạp khi cần (danh sách khách sạn) — có vùng chứa hàng nghìn id.</summary>
    [BsonElement("external_property_ids")] public List<string>? PropertyIds { get; set; }
    [BsonElement("external_property_expanded_ids")] public List<string>? ExpandedPropertyIds { get; set; }
    /// <summary>Không có trong DB — tính bằng $size(external_property_ids) khi truy vấn.</summary>
    [BsonElement("hotel_count")] public int? HotelCount { get; set; }
}

public sealed class HbeAncestor
{
    // Hai trường này viết hoa trong DB ("RegionId", "Type").
    [BsonElement("RegionId")] public string? RegionId { get; set; }
    [BsonElement("Type")] public string? Type { get; set; }
}

public sealed class HbeRegionCoordinates
{
    [BsonElement("center_latitude")] public double? Latitude { get; set; }
    [BsonElement("center_longitude")] public double? Longitude { get; set; }
}

/// <summary>
/// Collection <c>places</c>: chỉ mục tìm kiếm (text index trên <c>en_name</c> + <c>vi_non_mark_name</c>).
/// type = "Region" (trỏ tới epa_regions qua epa_region_id) hoặc "Property" (trỏ tới *_eap_properties qua epa_external_property_id).
/// </summary>
public sealed class HbePlace
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("type")] public string? Type { get; set; }
    [BsonElement("region_type")] public string? RegionType { get; set; }
    [BsonElement("epa_region_id")] public string? RegionId { get; set; }
    [BsonElement("epa_external_property_id")] public string? PropertyId { get; set; }
    [BsonElement("country_code")] public string? CountryCode { get; set; }
    [BsonElement("en_name")] public string? EnName { get; set; }
    [BsonElement("vi_name")] public string? ViName { get; set; }
    [BsonElement("ancestor_en_name")] public string? AncestorEnName { get; set; }
    [BsonElement("ancestor_vi_name")] public string? AncestorViName { get; set; }
}

/// <summary>Collection <c>vi_eap_properties</c> / <c>en_eap_properties</c> (cùng cấu trúc, khác ngôn ngữ).</summary>
public sealed class HbeProperty
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("external_property_id")] public string PropertyId { get; set; } = "";
    [BsonElement("name")] public string? Name { get; set; }
    [BsonElement("status")] public int? Status { get; set; }
    [BsonElement("phone")] public string? Phone { get; set; }
    [BsonElement("address")] public HbeAddress? Address { get; set; }
    [BsonElement("location")] public HbeLocation? Location { get; set; }
    [BsonElement("category")] public HbeItem? Category { get; set; }
    [BsonElement("ratings")] public HbeRatings? Ratings { get; set; }
    [BsonElement("images")] public List<HbeImage>? Images { get; set; }
    /// <summary>Khóa = epa_id trong references (type amenities_property).</summary>
    [BsonElement("amenities")] public Dictionary<string, HbeItem>? Amenities { get; set; }
    [BsonElement("attributes")] public HbeAttributes? Attributes { get; set; }
    [BsonElement("statistics")] public Dictionary<string, HbeItem>? Statistics { get; set; }
    [BsonElement("checkin")] public HbeCheckin? Checkin { get; set; }
    [BsonElement("checkout")] public HbeCheckout? Checkout { get; set; }
    [BsonElement("descriptions")] public HbeDescriptions? Descriptions { get; set; }
    [BsonElement("policies")] public HbePolicies? Policies { get; set; }
    [BsonElement("Fees")] public HbeFees? Fees { get; set; }
    /// <summary>Khóa = room id.</summary>
    [BsonElement("rooms")] public Dictionary<string, HbeRoom>? Rooms { get; set; }
}

/// <summary>Phần tử dạng {_id, name, value} dùng chung cho tiện nghi, thuộc tính, thống kê, loại hình...</summary>
public sealed class HbeItem
{
    [BsonId] public string? Id { get; set; }
    [BsonElement("name")] public string? Name { get; set; }
    [BsonElement("value")] public string? Value { get; set; }
}

public sealed class HbeAddress
{
    [BsonElement("line_1")] public string? Line1 { get; set; }
    [BsonElement("line_2")] public string? Line2 { get; set; }
    [BsonElement("city")] public string? City { get; set; }
    [BsonElement("country_code")] public string? CountryCode { get; set; }
}

public sealed class HbeLocation
{
    [BsonElement("coordinates")] public HbeCoordinates? Coordinates { get; set; }
}

public sealed class HbeCoordinates
{
    [BsonElement("latitude")] public double? Latitude { get; set; }
    [BsonElement("longitude")] public double? Longitude { get; set; }
}

public sealed class HbeRatings
{
    [BsonElement("property")] public HbePropertyRating? Property { get; set; }
    [BsonElement("guest")] public HbeGuestRating? Guest { get; set; }
}

/// <summary>Hạng sao, lưu dạng chuỗi ("4.5").</summary>
public sealed class HbePropertyRating
{
    [BsonElement("rating")] public string? Rating { get; set; }
    [BsonElement("type")] public string? Type { get; set; }
}

/// <summary>Điểm khách đánh giá — thang 5, lưu dạng chuỗi ("4.6").</summary>
public sealed class HbeGuestRating
{
    [BsonElement("count")] public int? Count { get; set; }
    [BsonElement("overall")] public string? Overall { get; set; }
    [BsonElement("cleanliness")] public string? Cleanliness { get; set; }
    [BsonElement("service")] public string? Service { get; set; }
    [BsonElement("comfort")] public string? Comfort { get; set; }
    [BsonElement("condition")] public string? Condition { get; set; }
    [BsonElement("amenities")] public string? Amenities { get; set; }
}

public sealed class HbeImage
{
    [BsonElement("hero_image")] public bool? HeroImage { get; set; }
    [BsonElement("category")] public int? Category { get; set; }
    [BsonElement("caption")] public string? Caption { get; set; }
    /// <summary>Khóa "70px", "350px", "1000px".</summary>
    [BsonElement("links")] public Dictionary<string, HbeLink>? Links { get; set; }
}

public sealed class HbeLink
{
    [BsonElement("href")] public string? Href { get; set; }
}

public sealed class HbeAttributes
{
    [BsonElement("general")] public Dictionary<string, HbeItem>? General { get; set; }
    [BsonElement("pets")] public Dictionary<string, HbeItem>? Pets { get; set; }
}

public sealed class HbeCheckin
{
    [BsonElement("begin_time")] public string? BeginTime { get; set; }
    [BsonElement("end_time")] public string? EndTime { get; set; }
    [BsonElement("instructions")] public string? Instructions { get; set; }
    [BsonElement("special_instructions")] public string? SpecialInstructions { get; set; }
}

public sealed class HbeCheckout
{
    [BsonElement("time")] public string? Time { get; set; }
}

public sealed class HbeDescriptions
{
    [BsonElement("headline")] public string? Headline { get; set; }
    [BsonElement("location")] public string? Location { get; set; }
    [BsonElement("rooms")] public string? Rooms { get; set; }
    [BsonElement("amenities")] public string? Amenities { get; set; }
    [BsonElement("dining")] public string? Dining { get; set; }
    [BsonElement("business_amenities")] public string? BusinessAmenities { get; set; }
    [BsonElement("attractions")] public string? Attractions { get; set; }
}

public sealed class HbePolicies
{
    [BsonElement("know_before_you_go")] public string? KnowBeforeYouGo { get; set; }
}

public sealed class HbeFees
{
    [BsonElement("mandatory")] public string? Mandatory { get; set; }
    [BsonElement("optional")] public string? Optional { get; set; }
}

public sealed class HbeRoom
{
    [BsonId] public string? Id { get; set; }
    [BsonElement("name")] public string? Name { get; set; }
    [BsonElement("descriptions")] public HbeRoomDescriptions? Descriptions { get; set; }
    [BsonElement("area")] public HbeArea? Area { get; set; }
    [BsonElement("bed_groups")] public Dictionary<string, HbeBedGroup>? BedGroups { get; set; }
    [BsonElement("occupancy")] public HbeOccupancy? Occupancy { get; set; }
    [BsonElement("images")] public List<HbeImage>? Images { get; set; }
    [BsonElement("amenities")] public Dictionary<string, HbeItem>? Amenities { get; set; }
    [BsonElement("views")] public Dictionary<string, HbeItem>? Views { get; set; }
}

public sealed class HbeRoomDescriptions
{
    [BsonElement("overview")] public string? Overview { get; set; }
}

/// <summary>Diện tích, lưu dạng chuỗi ("26.0").</summary>
public sealed class HbeArea
{
    [BsonElement("square_meters")] public string? SquareMeters { get; set; }
    [BsonElement("square_feet")] public string? SquareFeet { get; set; }
}

public sealed class HbeBedGroup
{
    [BsonId] public string? Id { get; set; }
    [BsonElement("description")] public string? Description { get; set; }
}

public sealed class HbeOccupancy
{
    [BsonElement("max_allowed")] public HbeMaxAllowed? MaxAllowed { get; set; }
}

public sealed class HbeMaxAllowed
{
    [BsonElement("adults")] public int? Adults { get; set; }
    [BsonElement("children")] public int? Children { get; set; }
    [BsonElement("total")] public int? Total { get; set; }
}

/// <summary>
/// Bản tóm tắt một khách sạn, tính sẵn bằng aggregation (không tải cả document ~30–60 KB).
/// Tên thuộc tính theo camelCase (convention toàn cục) khớp với tên trường trong $project.
/// </summary>
public sealed class HbePropertyRow
{
    /// <summary>= external_property_id (được $project vào _id).</summary>
    [BsonId] public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? City { get; set; }
    public string? Line1 { get; set; }
    public string? CountryCode { get; set; }
    public double Stars { get; set; }
    /// <summary>Thang 5 như trong DB.</summary>
    public double Overall { get; set; }
    public int ReviewCount { get; set; }
    public string? Image { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public List<string> AmenityIds { get; set; } = [];
    public List<string> PetIds { get; set; } = [];
}
