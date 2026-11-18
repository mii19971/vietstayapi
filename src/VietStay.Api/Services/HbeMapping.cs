using System.Text;
using VietStay.Api.Contracts;
using VietStay.Api.Domain.Hbe;

namespace VietStay.Api.Services;

/// <summary>
/// Mã tiện nghi chính dùng cho bộ lọc và biểu tượng trên app, ánh xạ sang epa_id trong references
/// (amenities_property → document.amenities, attributes_pets → document.attributes.pets).
/// Khách sạn có ÍT NHẤT MỘT id trong nhóm được xem là có tiện nghi đó.
/// </summary>
public static class AmenityMap
{
    private sealed record Rule(string[] AmenityIds, string[] PetIds);

    private static readonly Dictionary<string, Rule> Rules = new()
    {
        ["wifi"] = new(["2390"], []),                                                   // Free WiFi
        ["pool"] = new(["2074", "2138", "2820", "2821", "1073744115", "1073744117", "1073744119"], []),
        ["spa"] = new(["2017", "2123", "2129"], []),
        ["gym"] = new(["9", "4468"], []),                                               // Fitness facilities
        ["restaurant"] = new(["2537"], []),                                             // Number of restaurants
        ["parking"] = new(["3861", "3862", "3863", "3864", "3761", "1073742906"], []),
        ["breakfast"] = new(["2001", "4646", "4647", "4648", "4649", "4650", "1073743434", "1073744141"], []), // bữa sáng miễn phí
        ["airport"] = new(["10", "56", "2353", "4004", "4005", "4006", "4007"], []),
        ["beach"] = new(["2080", "4431", "5015"], []),
        ["pet"] = new([], ["51", "1073742807"]),                                        // Pets allowed
    };

    public static IReadOnlyCollection<string> Keys => Rules.Keys;

    public static bool IsKnown(string key) => Rules.ContainsKey(key);

    public static bool Has(string key, IReadOnlyCollection<string> amenityIds, IReadOnlyCollection<string> petIds) =>
        Rules.TryGetValue(key, out var r) && (r.AmenityIds.Any(amenityIds.Contains) || r.PetIds.Any(petIds.Contains));

    public static List<string> KeysOf(IReadOnlyCollection<string> amenityIds, IReadOnlyCollection<string> petIds) =>
        Rules.Keys.Where(k => Has(k, amenityIds, petIds)).ToList();
}

public static class HbeMapping
{
    /// <summary>Điểm khách (thang 5 trong DB) → thang 10, làm tròn 1 chữ số.</summary>
    public static double ToTen(double? fiveScale) => fiveScale is > 0 ? Math.Round(fiveScale.Value * 2, 1) : 0;

    public static HotelSummaryDto ToSummary(this HbePropertyRow r)
    {
        var amenityIds = r.AmenityIds.ToHashSet();
        var petIds = r.PetIds.ToHashSet();
        return new HotelSummaryDto(
            r.Id, NullIfEmpty(r.Name) ?? "", NullIfEmpty(r.Category), NullIfEmpty(r.City) ?? "", JoinAddress(r.Line1, r.City), r.CountryCode,
            r.Stars, ToTen(r.Overall), r.ReviewCount, r.Image ?? "",
            AmenityMap.KeysOf(amenityIds, petIds), r.Lat, r.Lng);
    }

    public static RegionDto ToDto(this HbeRegion r, Lang lang) =>
        new(r.RegionId, lang.Pick(r.ViName, r.EnName), r.Type, NullIfEmpty(lang.Pick(r.AncestorViName, r.AncestorEnName)),
            r.CountryCode, r.ParentId, r.Coordinates?.Latitude, r.Coordinates?.Longitude, r.HotelCount ?? 0);

    public static string JoinAddress(string? line1, string? city)
    {
        var parts = new[] { line1, city }.Select(NullIfEmpty).OfType<string>().ToList();
        // Tránh lặp "…, Đà Nẵng, Đà Nẵng" khi line_1 đã chứa tên thành phố.
        if (parts.Count == 2 && parts[0].EndsWith(parts[1], StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(1);
        return string.Join(", ", parts);
    }

    public static string Address(this HbeProperty p) => JoinAddress(p.Address?.Line1, p.Address?.City);

    /// <summary>Ảnh chính trước, sau đó theo thứ tự trong DB.</summary>
    public static List<ImageDto> ToImages(IEnumerable<HbeImage>? images) =>
        (images ?? [])
        .OrderByDescending(i => i.HeroImage == true)
        .Select(i =>
        {
            var big = Link(i, "1000px") ?? Link(i, "350px") ?? Link(i, "70px");
            var thumb = Link(i, "350px") ?? big;
            return big is null ? null : new ImageDto(big, thumb!, NullIfEmpty(i.Caption));
        })
        .OfType<ImageDto>()
        .ToList();

    public static string HeroImage(this HbeProperty p) => ToImages(p.Images).FirstOrDefault()?.Url ?? "";

    private static string? Link(HbeImage i, string size) =>
        i.Links is not null && i.Links.TryGetValue(size, out var l) && !string.IsNullOrWhiteSpace(l.Href) ? l.Href : null;

    /// <summary>"Số lượng nhà hàng:  - " + "2" → "Số lượng nhà hàng: 2".</summary>
    public static NamedItemDto ToNamed(string key, HbeItem item)
    {
        var name = (item.Name ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(item.Value))
        {
            name = $"{name.TrimEnd(' ', '-', ':')}: {item.Value.Trim()}";
        }
        return new NamedItemDto(item.Id ?? key, name, NullIfEmpty(item.Value));
    }

    private static List<NamedItemDto> Named(Dictionary<string, HbeItem>? items) =>
        (items ?? []).Select(kv => ToNamed(kv.Key, kv.Value)).Where(n => n.Name.Length > 0).ToList();

    private static List<string> Names(Dictionary<string, HbeItem>? items) => Named(items).Select(n => n.Name).ToList();

    public static RoomDto ToDto(string key, HbeRoom r)
    {
        var size = Text.ParseDouble(r.Area?.SquareMeters);
        var max = r.Occupancy?.MaxAllowed;
        return new RoomDto(
            r.Id ?? key,
            (r.Name ?? "").Trim(),
            Text.HtmlToText(r.Descriptions?.Overview),
            size is > 0 ? size : null,
            (r.BedGroups ?? []).Values.Select(b => b.Description?.Trim()).OfType<string>().Where(s => s.Length > 0).ToList(),
            max?.Total, max?.Adults, max?.Children,
            ToImages(r.Images),
            Names(r.Amenities),
            Names(r.Views));
    }

    private static readonly (string Key, string Vi, string En, Func<HbeDescriptions, string?> Get)[] Sections =
    [
        ("rooms", "Phòng nghỉ", "Rooms", d => d.Rooms),
        ("amenities", "Tiện nghi", "Amenities", d => d.Amenities),
        ("dining", "Ẩm thực", "Dining", d => d.Dining),
        ("business", "Dịch vụ doanh nhân", "Business amenities", d => d.BusinessAmenities),
        ("attractions", "Điểm tham quan lân cận", "Nearby attractions", d => d.Attractions),
    ];

    public static HotelDetailDto ToDetail(this HbeProperty p, Lang lang)
    {
        var guest = p.Ratings?.Guest;
        var amenityIds = (p.Amenities ?? []).Keys.ToHashSet();
        var petIds = (p.Attributes?.Pets ?? []).Keys.ToHashSet();
        var descriptions = p.Descriptions ?? new HbeDescriptions();

        RatingBreakdownDto? breakdown = guest is null ? null : new RatingBreakdownDto(
            Ten(guest.Cleanliness), Ten(guest.Service), Ten(guest.Comfort), Ten(guest.Condition), Ten(guest.Amenities));
        if (breakdown is { Cleanliness: null, Service: null, Comfort: null, Condition: null, Amenities: null }) breakdown = null;

        return new HotelDetailDto(
            p.PropertyId,
            NullIfEmpty(p.Name) ?? "",
            NullIfEmpty(p.Category?.Name),
            NullIfEmpty(p.Address?.City) ?? "",
            p.Address(),
            p.Address?.CountryCode,
            Text.ParseDouble(p.Ratings?.Property?.Rating) ?? 0,
            ToTen(Text.ParseDouble(guest?.Overall)),
            guest?.Count ?? 0,
            breakdown,
            NullIfEmpty(Text.HtmlToText(descriptions.Headline)),
            Text.HtmlToText(descriptions.Location),
            Sections
                .Select(s => new DescriptionSectionDto(s.Key, lang == Lang.En ? s.En : s.Vi, Text.HtmlToText(s.Get(descriptions))))
                .Where(s => s.Text.Length > 0)
                .ToList(),
            ToImages(p.Images),
            AmenityMap.KeysOf(amenityIds, petIds),
            Named(p.Amenities).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            (p.Rooms ?? []).Select(kv => ToDto(kv.Key, kv.Value)).ToList(),
            new PoliciesDto(
                NullIfEmpty(p.Checkin?.BeginTime),
                NullIfEmpty(p.Checkin?.EndTime),
                NullIfEmpty(p.Checkout?.Time),
                NullIfEmpty(Text.HtmlToText(p.Checkin?.Instructions)),
                NullIfEmpty(Text.HtmlToText(p.Checkin?.SpecialInstructions)),
                NullIfEmpty(Text.HtmlToText(p.Policies?.KnowBeforeYouGo)),
                NullIfEmpty(Text.HtmlToText(p.Fees?.Mandatory)),
                NullIfEmpty(Text.HtmlToText(p.Fees?.Optional)),
                Names(p.Attributes?.Pets),
                Names(p.Attributes?.General)),
            Named(p.Statistics),
            NullIfEmpty(p.Phone),
            p.Location?.Coordinates?.Latitude,
            p.Location?.Coordinates?.Longitude);
    }

    private static double? Ten(string? fiveScale) => Text.ParseDouble(fiveScale) is > 0 and var v ? ToTen(v) : null;

    public static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Normalize(NormalizationForm.FormC);
}
