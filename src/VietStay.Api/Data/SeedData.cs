using MongoDB.Driver;
using VietStay.Api.Domain;

namespace VietStay.Api.Data;

/// <summary>
/// Dữ liệu mẫu, giống hệt src/data/hotels.ts của app VietStay (cùng id khách sạn/phòng),
/// để frontend chuyển từ mock sang API mà không đổi dữ liệu.
/// </summary>
public static class SeedData
{
    private static string U(string id, int w = 1200) =>
        $"https://images.unsplash.com/{id}?auto=format&fit=crop&w={w}&q=75";

    private static readonly string[] HotelImages =
    [
        U("photo-1566073771259-6a8506099945"), U("photo-1542314831-068cd1dbfeeb"),
        U("photo-1551882547-ff40c63fe5fa"), U("photo-1520250497591-112f2f40a3f4"),
        U("photo-1571896349842-33c89424de2d"), U("photo-1582719478250-c89cae4dc85b"),
        U("photo-1564501049412-61c2a3083791"), U("photo-1584132967334-10e028bd69f7"),
    ];

    private static readonly string[] RoomImages =
    [
        U("photo-1611892440504-42a792e24d32", 800), U("photo-1590490360182-c33d57733427", 800),
        U("photo-1631049307264-da0ec9d70304", 800), U("photo-1618773928121-c32242e63f39", 800),
    ];

    public static readonly City[] Cities =
    [
        new() { Id = "ha-noi", Name = "Hà Nội", Image = U("photo-1509030450996-dd1a26dda07a", 600) },
        new() { Id = "ho-chi-minh", Name = "TP. Hồ Chí Minh", Image = U("photo-1583417319070-4a69db38a482", 600) },
        new() { Id = "da-nang", Name = "Đà Nẵng", Image = U("photo-1559592413-7cec4d0cae2b", 600) },
        new() { Id = "hoi-an", Name = "Hội An", Image = U("photo-1557750255-c76072a7aad1", 600) },
        new() { Id = "nha-trang", Name = "Nha Trang", Image = U("photo-1573270689103-d7a4e42b609a", 600) },
        new() { Id = "phu-quoc", Name = "Phú Quốc", Image = U("photo-1540541338287-41700207dee6", 600) },
        new() { Id = "da-lat", Name = "Đà Lạt", Image = U("photo-1555921015-5532091f6026", 600) },
        new() { Id = "sa-pa", Name = "Sa Pa", Image = U("photo-1528127269322-539801943592", 600) },
    ];

    private static readonly (string Author, double Rating, string Comment)[] ReviewPool =
    [
        ("Minh Anh", 9.2, "Phòng sạch sẽ, nhân viên thân thiện, vị trí rất thuận tiện."),
        ("Quốc Bảo", 8.6, "Bữa sáng ngon và đa dạng. Sẽ quay lại lần sau."),
        ("Thu Hà", 9.6, "View đẹp tuyệt vời, check-in nhanh chóng."),
        ("Đức Huy", 7.8, "Giá hợp lý, tuy nhiên cách âm chưa tốt lắm."),
        ("Lan Phương", 8.9, "Hồ bơi rộng, rất phù hợp cho gia đình có trẻ nhỏ."),
        ("Hoàng Nam", 9.0, "Dịch vụ chu đáo, giường êm, ngủ rất ngon."),
    ];

    private sealed record HotelSeed(
        string Id, string Name, string CityId, string Address, int Stars, double Rating, int ReviewCount,
        string Description, string[] Amenities, long BasePrice);

    private static readonly HotelSeed[] Seeds =
    [
        new("hn-lotus-old-quarter", "Lotus Old Quarter Hotel", "ha-noi", "28 Hàng Bạc, Hoàn Kiếm, Hà Nội", 4, 9.1, 1284,
            "Nằm giữa lòng phố cổ, chỉ vài phút đi bộ tới Hồ Gươm. Thiết kế giao thoa giữa nét Đông Dương và hiện đại.",
            ["wifi", "restaurant", "breakfast", "ac", "airport"], 1_150_000),
        new("hn-west-lake-retreat", "West Lake Retreat", "ha-noi", "115 Quảng An, Tây Hồ, Hà Nội", 5, 9.4, 862,
            "Khu nghỉ dưỡng yên tĩnh bên Hồ Tây với hồ bơi vô cực, spa và nhà hàng ẩm thực Việt cao cấp.",
            ["wifi", "pool", "spa", "gym", "restaurant", "parking", "breakfast", "ac"], 2_600_000),
        new("hcm-saigon-skyline", "Saigon Skyline Hotel", "ho-chi-minh", "76 Lê Lợi, Quận 1, TP. Hồ Chí Minh", 5, 8.9, 2310,
            "Khách sạn trung tâm Quận 1 với rooftop bar nhìn toàn cảnh thành phố và hồ bơi trên tầng thượng.",
            ["wifi", "pool", "gym", "restaurant", "spa", "ac", "airport"], 2_200_000),
        new("hcm-district3-boutique", "District 3 Boutique", "ho-chi-minh", "52 Võ Văn Tần, Quận 3, TP. Hồ Chí Minh", 3, 8.4, 540,
            "Khách sạn boutique nhỏ xinh trên con phố rợp bóng cây, gần nhiều quán cà phê nổi tiếng.",
            ["wifi", "breakfast", "ac", "pet"], 750_000),
        new("dn-my-khe-ocean", "My Khe Ocean Resort", "da-nang", "200 Võ Nguyên Giáp, Sơn Trà, Đà Nẵng", 5, 9.3, 1795,
            "Resort ven biển Mỹ Khê với bãi biển riêng, ba hồ bơi và câu lạc bộ trẻ em.",
            ["wifi", "pool", "spa", "gym", "restaurant", "beach", "parking", "breakfast", "ac", "airport"], 2_900_000),
        new("dn-han-river-view", "Han River View Hotel", "da-nang", "35 Bạch Đằng, Hải Châu, Đà Nẵng", 4, 8.7, 980,
            "Ngắm cầu Rồng phun lửa ngay từ ban công phòng. Gần chợ Hàn và các điểm ăn uống.",
            ["wifi", "pool", "restaurant", "breakfast", "ac", "parking"], 1_250_000),
        new("ha-lantern-garden", "Lantern Garden Villa", "hoi-an", "12 Trần Hưng Đạo, Minh An, Hội An", 4, 9.5, 1120,
            "Biệt thự sân vườn đậm chất phố Hội, miễn phí xe đạp và lớp học nấu ăn truyền thống.",
            ["wifi", "pool", "breakfast", "restaurant", "ac", "airport"], 1_350_000),
        new("nt-coral-bay", "Coral Bay Nha Trang", "nha-trang", "64 Trần Phú, Lộc Thọ, Nha Trang", 4, 8.6, 1430,
            "Đối diện bãi biển Trần Phú, hồ bơi tầng thượng và nhà hàng hải sản tươi sống.",
            ["wifi", "pool", "restaurant", "beach", "breakfast", "ac", "gym"], 1_100_000),
        new("pq-sunset-sanctuary", "Sunset Sanctuary Phú Quốc", "phu-quoc", "Bãi Trường, Dương Tơ, Phú Quốc", 5, 9.6, 760,
            "Những căn villa có hồ bơi riêng hướng về hoàng hôn trên biển. Thiên đường cho kỳ nghỉ trăng mật.",
            ["wifi", "pool", "spa", "gym", "restaurant", "beach", "breakfast", "ac", "airport"], 3_800_000),
        new("dl-pine-hill", "Pine Hill Lodge", "da-lat", "9 Trần Hưng Đạo, Phường 10, Đà Lạt", 3, 8.8, 615,
            "Nhà nghỉ gỗ giữa đồi thông, lò sưởi ấm áp và vườn hoa quanh năm.",
            ["wifi", "breakfast", "parking", "pet", "restaurant"], 680_000),
        new("sp-cloud-terrace", "Cloud Terrace Sa Pa", "sa-pa", "18 Mường Hoa, Sa Pa, Lào Cai", 4, 9.0, 488,
            "Phòng kính nhìn thẳng ra thung lũng Mường Hoa và ruộng bậc thang, có bể sục nước nóng ngoài trời.",
            ["wifi", "spa", "restaurant", "breakfast", "parking"], 1_450_000),
    ];

    private static long Round10k(double v) => (long)Math.Round(v / 10_000, MidpointRounding.AwayFromZero) * 10_000;

    private static List<Room> MakeRooms(string hotelId, long basePrice, int seed)
    {
        string Img(int i) => RoomImages[(seed + i) % RoomImages.Length];
        return
        [
            new()
            {
                Id = $"{hotelId}-std", Name = "Phòng Tiêu chuẩn",
                Description = "Phòng ấm cúng với đầy đủ tiện nghi cơ bản, phù hợp cho chuyến đi ngắn ngày.",
                Size = 22, Beds = "1 giường đôi", MaxGuests = 2, Price = basePrice, Image = Img(0), Inventory = 6,
                FreeCancellation = false, BreakfastIncluded = false,
            },
            new()
            {
                Id = $"{hotelId}-dlx", Name = "Phòng Deluxe",
                Description = "Không gian rộng rãi, cửa sổ lớn đón ánh sáng tự nhiên, bao gồm bữa sáng.",
                Size = 30, Beds = "1 giường King", MaxGuests = 2, Price = Round10k(basePrice * 1.4), Image = Img(1),
                Inventory = 4, FreeCancellation = true, BreakfastIncluded = true,
            },
            new()
            {
                Id = $"{hotelId}-fam", Name = "Phòng Gia đình",
                Description = "Hai giường lớn, khu vực sinh hoạt riêng, lý tưởng cho gia đình 4 người.",
                Size = 40, Beds = "2 giường Queen", MaxGuests = 4, Price = Round10k(basePrice * 1.8), Image = Img(2),
                Inventory = 3, FreeCancellation = true, BreakfastIncluded = true,
            },
            new()
            {
                Id = $"{hotelId}-ste", Name = "Suite Hướng cảnh",
                Description = "Phòng khách riêng biệt, bồn tắm nằm và tầm nhìn toàn cảnh tuyệt đẹp.",
                Size = 55, Beds = "1 giường King + sofa giường", MaxGuests = 3, Price = Round10k(basePrice * 2.6),
                Image = Img(3), Inventory = 1, FreeCancellation = true, BreakfastIncluded = true,
            },
        ];
    }

    private static List<Review> MakeReviews(int seed) =>
        Enumerable.Range(0, 3).Select(i =>
        {
            var r = ReviewPool[(seed + i * 2) % ReviewPool.Length];
            return new Review
            {
                Id = $"rv-{seed}-{i}", Author = r.Author, Rating = r.Rating, Comment = r.Comment,
                Date = new DateOnly(2026, (seed + i) % 9 + 1, 10 + i),
            };
        }).ToList();

    public static IReadOnlyList<Hotel> Hotels { get; } = Seeds.Select((s, i) =>
    {
        var city = Cities.First(c => c.Id == s.CityId).Name;
        return new Hotel
        {
            Id = s.Id, Name = s.Name, CityId = s.CityId, City = city, Address = s.Address, Stars = s.Stars,
            Rating = s.Rating, ReviewCount = s.ReviewCount, Description = s.Description,
            Amenities = [.. s.Amenities],
            Images = Enumerable.Range(0, 4).Select(k => HotelImages[(i + k * 3) % HotelImages.Length]).ToList(),
            Rooms = MakeRooms(s.Id, s.BasePrice, i),
            Reviews = MakeReviews(i),
            SearchText = Text.Normalize($"{s.Name} {city} {s.Address}"),
        };
    }).ToList();

    /// <summary>Ghi đè (upsert) danh mục thành phố/khách sạn mẫu. Không đụng tới booking/user.</summary>
    public static async Task UpsertAsync(MongoDb db, CancellationToken ct)
    {
        for (var i = 0; i < Cities.Length; i++) Cities[i].SortOrder = i;

        await db.Cities.BulkWriteAsync(
            Cities.Select(c => new ReplaceOneModel<City>(Builders<City>.Filter.Eq(x => x.Id, c.Id), c) { IsUpsert = true }),
            cancellationToken: ct);
        await db.Hotels.BulkWriteAsync(
            Hotels.Select(h => new ReplaceOneModel<Hotel>(Builders<Hotel>.Filter.Eq(x => x.Id, h.Id), h) { IsUpsert = true }),
            cancellationToken: ct);
    }
}
