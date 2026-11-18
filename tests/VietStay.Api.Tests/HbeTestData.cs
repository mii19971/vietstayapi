using MongoDB.Bson;
using MongoDB.Driver;

namespace VietStay.Api.Tests;

/// <summary>
/// Bản thu nhỏ của database hbe (cùng tên collection, tên trường và kiểu dữ liệu như DB thật:
/// điểm/sao dạng chuỗi, tiện nghi là object theo epa_id, text index trên places).
/// </summary>
public static class HbeTestData
{
    public const string DaNang = "6297250";
    public const string HoiAn = "6034264";
    public const string MyKhe = "dn-poi-my-khe";          // vùng nhỏ chỉ có danh sách "lân cận"
    public const string Ocean = "1001";                   // 5 sao, 9.2, pool + spa + wifi
    public const string River = "1002";                   // 3 sao, 8.0, wifi, cho thú cưng
    public const string Hostel = "1003";                  // chưa xếp hạng, chưa có đánh giá
    public const string Lantern = "2001";                 // Hội An

    public static async Task SeedAsync(IMongoDatabase db)
    {
        await db.GetCollection<BsonDocument>("country").InsertManyAsync(
        [
            new() { { "country_code", "VN" }, { "en_country_name", "Vietnam" }, { "vi_country_name", "Việt Nam" }, { "region_id", "966252357" } },
            new() { { "country_code", "JP" }, { "en_country_name", "Japan" }, { "vi_country_name", "Nhật Bản" }, { "region_id", "966240000" } },
            new() { { "country_code", BsonNull.Value }, { "region_id", "1" } },
        ]);

        await db.GetCollection<BsonDocument>("references").InsertManyAsync(
        [
            Ref("2390", "amenities_property", "Free WiFi", "Wifi miễn phí"),
            Ref("2074", "amenities_property", "Swimming pool", "Hồ bơi"),
            Ref("2017", "amenities_property", "Spa services on site", "Spa trong khuôn viên"),
            Ref("51", "attributes_pets", "Pets allowed", "Có thể mang theo vật nuôi"),
        ]);

        await db.GetCollection<BsonDocument>("epa_regions").InsertManyAsync(
        [
            Region(DaNang, "Da Nang", "Đà Nẵng", "city", 16.06, 108.21, [Ocean, River, Hostel], [Ocean, River, Hostel, Lantern]),
            Region(HoiAn, "Hoi An", "Hội An", "city", 15.88, 108.33, [Lantern], [Lantern]),
            Region(MyKhe, "My Khe Beach", "Bãi biển Mỹ Khê", "point_of_interest", 16.06, 108.24, [], [Ocean]),
        ]);

        var places = db.GetCollection<BsonDocument>("places");
        await places.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Text("en_name").Text("vi_non_mark_name"),
            new CreateIndexOptions { DefaultLanguage = "english" }));
        await places.InsertManyAsync(
        [
            Place("Region", DaNang, null, "city", 8, "Da Nang", "Đà Nẵng", "Da Nang", "Vietnam", "Việt Nam"),
            Place("Region", HoiAn, null, "city", 8, "Hoi An", "Hội An", "Hoi An", "Vietnam", "Việt Nam"),
            Place("Region", MyKhe, null, "point_of_interest", 3, "My Khe Beach", "Bãi biển Mỹ Khê", "Bai bien My Khe", "Da Nang, Vietnam", "Đà Nẵng, Việt Nam"),
            Place("Property", null, Ocean, null, 0, "Ocean Pearl Da Nang", "Ocean Pearl Đà Nẵng", "Ocean Pearl Da Nang", "Da Nang, Vietnam", "Đà Nẵng, Việt Nam"),
            Place("Property", null, Lantern, null, 0, "Lantern Garden Hoi An", "Lantern Garden Hội An", "Lantern Garden Hoi An", "Hoi An, Vietnam", "Hội An, Việt Nam"),
        ]);

        await db.GetCollection<BsonDocument>("vi_eap_properties").InsertManyAsync(
        [
            Property(Ocean, "Ocean Pearl Đà Nẵng", "Đà Nẵng", "5.0", "4.6", 812, ["2390", "2074", "2017"], [], vi: true, withRooms: true),
            Property(River, "River View Đà Nẵng", "Đà Nẵng", "3.0", "4.0", 120, ["2390"], ["51"], vi: true),
            Property(Hostel, "Hostel Biển Xanh", "Đà Nẵng", null, null, null, [], [], vi: true),
            Property(Lantern, "Lantern Garden Hội An", "Hội An", "4.0", "4.4", 300, ["2390", "2074"], [], vi: true),
        ]);
        await db.GetCollection<BsonDocument>("en_eap_properties").InsertManyAsync(
        [
            Property(Ocean, "Ocean Pearl Da Nang", "Da Nang", "5.0", "4.6", 812, ["2390", "2074", "2017"], [], vi: false, withRooms: true),
            Property(River, "River View Da Nang", "Da Nang", "3.0", "4.0", 120, ["2390"], ["51"], vi: false),
            Property(Hostel, "Blue Sea Hostel", "Da Nang", null, null, null, [], [], vi: false),
            Property(Lantern, "Lantern Garden Hoi An", "Hoi An", "4.0", "4.4", 300, ["2390", "2074"], [], vi: false),
        ]);
    }

    private static BsonDocument Ref(string epaId, string type, string en, string vi) =>
        new() { { "epa_id", epaId }, { "type", type }, { "en_name", en }, { "vi_name", vi }, { "system_id", int.Parse(epaId) } };

    private static BsonDocument Region(string id, string en, string vi, string type, double lat, double lng,
        string[] ids, string[] expanded) => new()
    {
        { "region_id", id }, { "en_region_name", en }, { "vi_region_name", vi },
        { "en_region_type", type }, { "vi_region_type", type }, { "country_code", "VN" },
        { "ancestor_en_name", "Vietnam" }, { "ancestor_vi_name", "Việt Nam" }, // NFD như DB thật
        { "current_ancestor_id", "966252357" },
        { "ancestors", new BsonArray { new BsonDocument { { "RegionId", "966252357" }, { "Type", "country" } } } },
        { "coordinates", new BsonDocument { { "center_latitude", lat }, { "center_longitude", lng } } },
        { "external_property_ids", new BsonArray(ids) },
        { "external_property_expanded_ids", new BsonArray(expanded) },
    };

    private static BsonDocument Place(string type, string? regionId, string? propertyId, string? regionType, int regionRank,
        string en, string vi, string viNonMark, string ancestorEn, string ancestorVi) => new()
    {
        { "type", type }, { "type_rank", type == "Region" ? 1 : 2 },
        { "epa_region_id", (BsonValue?)regionId ?? BsonNull.Value },
        { "epa_external_property_id", (BsonValue?)propertyId ?? BsonNull.Value },
        { "region_type", (BsonValue?)regionType ?? BsonNull.Value }, { "region_type_rank", regionRank },
        { "country_code", "VN" }, { "country_rank", 99 },
        { "en_name", en }, { "vi_name", vi }, { "vi_non_mark_name", viNonMark },
        { "ancestor_en_name", ancestorEn }, { "ancestor_vi_name", ancestorVi },
    };

    private static BsonDocument Item(string id, string name, string? value = null) => new()
    {
        { "_id", id }, { "external_id", BsonNull.Value }, { "name", name },
        { "value", (BsonValue?)value ?? BsonNull.Value }, { "categories", BsonNull.Value },
    };

    private static BsonDocument Image(string id, bool hero, string caption) => new()
    {
        { "hero_image", hero }, { "category", 3 }, { "caption", caption },
        { "links", new BsonDocument
            {
                { "70px", new BsonDocument("href", $"https://img.test/{id}_t.jpg") },
                { "350px", new BsonDocument("href", $"https://img.test/{id}_b.jpg") },
                { "1000px", new BsonDocument("href", $"https://img.test/{id}_z.jpg") },
            } },
    };

    private static BsonDocument Property(string id, string name, string city, string? stars, string? overall, int? count,
        string[] amenities, string[] pets, bool vi, bool withRooms = false)
    {
        var amenityDoc = new BsonDocument();
        foreach (var a in amenities) amenityDoc[a] = Item(a, vi ? $"Tiện nghi {a}" : $"Amenity {a}");
        if (amenities.Length > 0) amenityDoc["2537"] = Item("2537", vi ? "Số lượng nhà hàng:  - " : "Number of restaurants - ", "2");
        var petDoc = new BsonDocument();
        foreach (var p in pets) petDoc[p] = Item(p, vi ? "Có thể mang theo vật nuôi" : "Pets allowed");

        var doc = new BsonDocument
        {
            { "external_property_id", id },
            { "name", name },
            { "status", 1 },
            { "phone", "84-236-1234567" },
            { "address", new BsonDocument { { "line_1", $"{id} Võ Nguyên Giáp" }, { "line_2", BsonNull.Value }, { "city", city }, { "country_code", "VN" } } },
            { "location", new BsonDocument("coordinates", new BsonDocument { { "latitude", 16.07 }, { "longitude", 108.24 } }) },
            { "category", Item("1", vi ? "Khách sạn" : "Hotel") },
            { "ratings", new BsonDocument
                {
                    { "property", stars is null ? BsonNull.Value : new BsonDocument { { "rating", stars }, { "type", "Star" } } },
                    { "guest", overall is null ? BsonNull.Value : new BsonDocument
                        {
                            { "count", count ?? 0 }, { "overall", overall }, { "cleanliness", "4.8" }, { "service", "4.5" },
                            { "comfort", BsonNull.Value }, { "condition", "4.4" }, { "amenities", "4.2" },
                        } },
                } },
            { "images", id == Hostel
                ? new BsonArray()
                : new BsonArray { Image($"{id}-lobby", false, vi ? "Sảnh" : "Lobby"), Image($"{id}-hero", true, vi ? "Ảnh chính" : "Featured") } },
            { "amenities", amenityDoc },
            { "attributes", new BsonDocument { { "pets", petDoc }, { "general", new BsonDocument("2549", Item("2549", vi ? "Không có thang máy" : "No elevators")) } } },
            { "statistics", new BsonDocument("52", Item("52", vi ? "Tổng số phòng:  - " : "Total number of rooms - ", "120")) },
            { "checkin", new BsonDocument
                {
                    { "begin_time", "14:00" }, { "end_time", vi ? "nửa đêm" : "midnight" },
                    { "instructions", "<ul>  <li>Giấy tờ tùy thân</li><li>Đặt cọc &amp; thẻ tín dụng</li>  </ul>" },
                    { "special_instructions", "Lễ tân <b>24 giờ</b>." },
                } },
            { "checkout", new BsonDocument("time", "12:00") },
            { "descriptions", new BsonDocument
                {
                    { "headline", vi ? "Gần biển Mỹ Khê" : "Near My Khe Beach" },
                    { "location", vi ? "Cách biển 2 phút<br />đi bộ." : "2 minutes<br />to the beach." },
                    { "rooms", "<p>Phòng có ban công.</p>" },
                    { "amenities", BsonNull.Value }, { "dining", BsonNull.Value }, { "business_amenities", BsonNull.Value },
                    { "attractions", "Cầu Rồng - 3 km <br /> Chợ Hàn - 4 km" },
                } },
            { "policies", new BsonDocument("know_before_you_go", "<ul><li>Hồ bơi mở 6:00–20:00.</li></ul>") },
            { "Fees", BsonNull.Value },
        };
        doc["rooms"] = !withRooms ? BsonNull.Value : new BsonDocument
        {
            { "r-deluxe", Room("r-deluxe", vi ? "Phòng Deluxe, hướng biển" : "Deluxe Room, Ocean View", 2, "32.0") },
            { "r-family", Room("r-family", vi ? "Phòng Gia đình" : "Family Room", 4, null) },
        };
        return doc;
    }

    private static BsonDocument Room(string id, string name, int maxTotal, string? sqm) => new()
    {
        { "_id", id },
        { "name", name },
        { "descriptions", new BsonDocument("overview", "<p><strong>1 giường King</strong></p><p>Phòng 32 m²</p>") },
        { "area", sqm is null ? BsonNull.Value : new BsonDocument { { "square_meters", sqm }, { "square_feet", "344.0" } } },
        { "bed_groups", new BsonDocument("37321", new BsonDocument { { "_id", "37321" }, { "description", "1 giường King" } }) },
        { "occupancy", new BsonDocument("max_allowed", new BsonDocument { { "adults", maxTotal }, { "children", 1 }, { "total", maxTotal } }) },
        { "images", new BsonArray { Image(id, true, "Phòng") } },
        { "amenities", new BsonDocument("1", Item("1", "Máy điều hòa")) },
        { "views", new BsonDocument("4134", Item("4134", "Hướng biển")) },
    };
}
