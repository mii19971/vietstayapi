using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using VietStay.Api.Contracts;
using VietStay.Api.Data;
using VietStay.Api.Domain;

namespace VietStay.Api.Services;

public sealed class HotelService(MongoDb db, InventoryService inventory, StayService stays)
{
    private static readonly string[] Sorts = ["recommended", "price-asc", "price-desc", "rating"];
    private const int MaxPageSize = 50;

    public async Task<IReadOnlyList<CityDto>> GetCitiesAsync(CancellationToken ct) =>
        (await db.Cities.Find(FilterDefinition<City>.Empty).SortBy(c => c.SortOrder).ToListAsync(ct))
        .Select(c => c.ToDto()).ToList();

    public async Task<IReadOnlyList<HotelSummaryDto>> GetFeaturedAsync(int limit, CancellationToken ct)
    {
        var hotels = await db.Hotels.Find(FilterDefinition<Hotel>.Empty)
            .SortByDescending(h => h.Rating)
            .Limit(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);
        return hotels.Select(h => h.ToSummary(h.Rooms.Min(r => r.Price))).ToList();
    }

    public async Task<HotelDetailDto> GetDetailAsync(string id, CancellationToken ct) =>
        (await GetHotelAsync(id, ct)).ToDetail();

    public async Task<Hotel> GetHotelAsync(string id, CancellationToken ct) =>
        await db.Hotels.Find(h => h.Id == id).FirstOrDefaultAsync(ct)
        ?? throw ApiException.NotFound("Không tìm thấy khách sạn.");

    public async Task<IReadOnlyList<HotelSummaryDto>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var hotels = await db.Hotels.Find(Builders<Hotel>.Filter.In(h => h.Id, ids)).ToListAsync(ct);
        return hotels.Select(h => h.ToSummary(h.Rooms.Min(r => r.Price))).ToList();
    }

    public async Task<IReadOnlyList<RoomOfferDto>> GetRoomOffersAsync(string hotelId, StayQuery query, CancellationToken ct)
    {
        var v = new Validation();
        var stay = stays.FromQuery(query, v);
        v.ThrowIfInvalid();

        var hotel = await GetHotelAsync(hotelId, ct);
        var peak = await inventory.GetPeakBookedAsync(hotel.Rooms.Select(r => r.Id).ToList(), stay, ct);
        return hotel.Rooms.Select(room =>
        {
            var remaining = InventoryService.Remaining(room, peak);
            var fits = InventoryService.Fits(room, stay);
            return new RoomOfferDto(room.ToDto(), remaining, fits, fits && remaining >= stay.Rooms,
                room.Price * stay.Nights * stay.Rooms);
        }).ToList();
    }

    public async Task<SearchResultDto> SearchAsync(SearchQuery q, CancellationToken ct)
    {
        var v = new Validation();
        var stay = stays.FromQuery(q.CheckIn, q.CheckOut, q.Guests, q.Rooms, v);
        var sort = string.IsNullOrWhiteSpace(q.Sort) ? "recommended" : q.Sort.Trim().ToLowerInvariant();
        var amenities = (q.Amenities ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).Distinct().ToList();
        var page = q.Page ?? 1;
        var pageSize = q.PageSize ?? 20;

        v.Check(Sorts.Contains(sort), "sort", $"Giá trị hợp lệ: {string.Join(", ", Sorts)}.")
         .Check(amenities.All(Amenities.All.Contains), "amenities", $"Tiện nghi hợp lệ: {string.Join(", ", Amenities.All)}.")
         .Check(q.MinStars is null or (>= 1 and <= 5), "minStars", "Hạng sao từ 1 đến 5.")
         .Check(q.MinRating is null or (>= 0 and <= 10), "minRating", "Điểm đánh giá từ 0 đến 10.")
         .Check(q.MaxPrice is null or > 0, "maxPrice", "Giá tối đa phải lớn hơn 0.")
         .Check(page >= 1, "page", "Trang bắt đầu từ 1.")
         .Check(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"pageSize từ 1 đến {MaxPageSize}.");
        v.ThrowIfInvalid();

        // 1) Lọc danh mục trong MongoDB.
        var f = Builders<Hotel>.Filter;
        var filter = f.Empty;
        foreach (var word in Text.Normalize(q.Q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            filter &= f.Regex(h => h.SearchText, new BsonRegularExpression(Regex.Escape(word)));
        }
        if (q.MinStars is { } stars) filter &= f.Gte(h => h.Stars, stars);
        if (q.MinRating is { } rating) filter &= f.Gte(h => h.Rating, rating);
        if (amenities.Count > 0) filter &= f.All(h => h.Amenities, amenities);
        var hotels = await db.Hotels.Find(filter).ToListAsync(ct);

        // 2) Tính phòng trống cho khoảng ngày và giá thấp nhất của phòng phù hợp.
        var peak = await inventory.GetPeakBookedAsync(hotels.SelectMany(h => h.Rooms).Select(r => r.Id).ToList(), stay, ct);
        var matches = new List<(Hotel Hotel, long MinPrice)>();
        foreach (var hotel in hotels)
        {
            var prices = hotel.Rooms
                .Where(r => InventoryService.Fits(r, stay) && InventoryService.Remaining(r, peak) >= stay.Rooms)
                .Select(r => r.Price)
                .ToList();
            if (prices.Count == 0) continue;
            var min = prices.Min();
            if (q.MaxPrice is { } max && min > max) continue;
            matches.Add((hotel, min));
        }

        IEnumerable<(Hotel Hotel, long MinPrice)> sorted = sort switch
        {
            "price-asc" => matches.OrderBy(m => m.MinPrice),
            "price-desc" => matches.OrderByDescending(m => m.MinPrice),
            "rating" => matches.OrderByDescending(m => m.Hotel.Rating),
            // "Đề xuất": cân bằng điểm đánh giá và số lượt đánh giá.
            _ => matches.OrderByDescending(m => m.Hotel.Rating + Math.Log10(Math.Max(1, m.Hotel.ReviewCount))),
        };

        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(m => m.Hotel.ToSummary(m.MinPrice)).ToList();
        return new SearchResultDto(items, matches.Count, page, pageSize, stay.ToDto());
    }
}
