using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using VietStay.Api.Contracts;
using VietStay.Api.Data;
using VietStay.Api.Domain.Hbe;

namespace VietStay.Api.Services;

/// <summary>
/// Danh mục khách sạn đọc từ database hbe (chỉ đọc).
/// <list type="bullet">
/// <item>places — gợi ý khi gõ (text index có sẵn trên en_name + vi_non_mark_name).</item>
/// <item>epa_regions — vùng và danh sách external_property_ids của vùng.</item>
/// <item>vi_eap_properties / en_eap_properties — nội dung khách sạn theo ngôn ngữ.</item>
/// <item>country, references — bảng tra cứu.</item>
/// </list>
/// Document khách sạn rất lớn (30–60 KB) và lần đọc đầu một vùng có thể mất vài giây, nên bản tóm tắt
/// (<see cref="HbePropertyRow"/>) của từng vùng được tính bằng aggregation rồi giữ trong bộ nhớ; lọc, sắp xếp,
/// phân trang chạy trên bản tóm tắt đó.
/// </summary>
public sealed class CatalogService(
    HbeDb hbe,
    IMemoryCache cache,
    IOptions<HbeOptions> options,
    ILogger<CatalogService> logger)
{
    public const int MaxPageSize = 50;
    public const int MaxSuggestLimit = 20;
    private static readonly string[] Sorts = ["recommended", "rating", "stars", "name"];

    public static readonly string[] ReferenceTypes =
    [
        "amenities_property", "amenities_rooms", "amenities_rates", "amenities_vls", "attributes_general",
        "attributes_pets", "bed_groups", "categories", "images", "room_views", "spoken_languages", "statistics", "themes",
    ];

    private HbeOptions O => options.Value;
    private TimeSpan CacheFor => TimeSpan.FromMinutes(Math.Max(1, O.CacheMinutes));

    // ---------- Quốc gia, tra cứu ----------

    public Task<IReadOnlyList<CountryDto>> GetCountriesAsync(Lang lang, CancellationToken ct) =>
        CachedAsync<IReadOnlyList<CountryDto>>($"countries:{lang}", async () =>
        {
            var all = await hbe.Countries.Find(c => c.CountryCode != null).ToListAsync();
            var compare = StringComparer.Create(CultureInfo.GetCultureInfo(lang == Lang.En ? "en-US" : "vi-VN"), true);
            return all
                .Select(c => new CountryDto(c.CountryCode!, lang.Pick(c.ViName, c.EnName), c.RegionId ?? ""))
                .Where(c => c.Name.Length > 0)
                .DistinctBy(c => c.Code)
                .OrderBy(c => c.Name, compare)
                .ToList();
        }, ct);

    public async Task<IReadOnlyList<ReferenceDto>> GetReferencesAsync(string? type, string? ids, Lang lang, CancellationToken ct)
    {
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        var v = new Validation();
        v.Check(type is not null && ReferenceTypes.Contains(type), "type", $"Loại hợp lệ: {string.Join(", ", ReferenceTypes)}.")
         .Check(idList.Count <= 500, "ids", "Tối đa 500 id mỗi lần.");
        v.ThrowIfInvalid();

        var all = await CachedAsync<IReadOnlyList<ReferenceDto>>($"references:{type}:{lang}", async () =>
        {
            var rows = await hbe.References.Find(r => r.Type == type && r.EpaId != null).ToListAsync();
            return rows
                .Select(r => new ReferenceDto(r.EpaId!, lang.Pick(r.ViName, r.EnName), HbeMapping.NullIfEmpty(r.Group)))
                .DistinctBy(r => r.Id)
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, ct);
        if (idList.Count == 0) return all;
        var wanted = idList.ToHashSet();
        return all.Where(r => wanted.Contains(r.Id)).ToList();
    }

    // ---------- Vùng, điểm đến ----------

    /// <summary>Trường cần cho RegionDto — không tải mảng external_property_ids (có vùng chứa hàng nghìn id).</summary>
    private static readonly BsonDocument RegionProjection = new()
    {
        { "region_id", 1 }, { "en_region_name", 1 }, { "vi_region_name", 1 }, { "en_region_type", 1 },
        { "country_code", 1 }, { "ancestor_en_name", 1 }, { "ancestor_vi_name", 1 }, { "current_ancestor_id", 1 },
        { "coordinates", 1 },
        { "hotel_count", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$external_property_ids", new BsonArray() })) },
    };

    private async Task<List<HbeRegion>> FindRegionsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        await hbe.Regions.Aggregate()
            .Match(Builders<HbeRegion>.Filter.In(r => r.RegionId, ids))
            .Project<HbeRegion>(RegionProjection)
            .ToListAsync(ct);

    public async Task<RegionDto> GetRegionAsync(string id, Lang lang, CancellationToken ct)
    {
        var region = (await FindRegionsAsync([id], ct)).FirstOrDefault()
                     ?? throw ApiException.NotFound("Không tìm thấy địa điểm.");
        return region.ToDto(lang);
    }

    public Task<IReadOnlyList<DestinationDto>> GetDestinationsAsync(Lang lang, CancellationToken ct) =>
        CachedAsync<IReadOnlyList<DestinationDto>>($"destinations:{lang}", async () =>
        {
            var configured = O.Destinations
                .Where(d => !string.IsNullOrWhiteSpace(d.RegionId))
                .DistinctBy(d => d.RegionId) // cấu hình ghép từ nhiều nguồn có thể trùng
                .ToList();
            var regions = (await FindRegionsAsync(configured.Select(d => d.RegionId).ToList(), CancellationToken.None))
                .ToDictionary(r => r.RegionId);
            return configured
                .Where(d => regions.ContainsKey(d.RegionId))
                .Select(d =>
                {
                    var r = regions[d.RegionId];
                    return new DestinationDto(r.RegionId, lang.Pick(r.ViName, r.EnName), d.Image, r.HotelCount ?? 0);
                })
                .ToList();
        }, ct);

    // ---------- Gợi ý khi gõ ----------

    public async Task<IReadOnlyList<PlaceSuggestionDto>> SuggestAsync(SuggestQuery q, CancellationToken ct)
    {
        var v = new Validation();
        var lang = Langs.Parse(q.Lang, v);
        var limit = q.Limit ?? 10;
        v.Check(limit is >= 1 and <= MaxSuggestLimit, "limit", $"limit từ 1 đến {MaxSuggestLimit}.");
        v.ThrowIfInvalid();
        return await SuggestCoreAsync(q.Q, limit, lang, ct);
    }

    /// <summary>
    /// Tìm trong places bằng text index: mọi từ (đã bỏ dấu) đều phải xuất hiện (mỗi từ là một "phrase").
    /// Khớp theo từ hoàn chỉnh — "da nang" khớp "Đà Nẵng", "da na" thì chưa.
    /// Ưu tiên: Việt Nam (country_rank) → vùng trước khách sạn (type_rank) → vùng lớn trước (region_type_rank).
    /// </summary>
    private async Task<List<PlaceSuggestionDto>> SuggestCoreAsync(string? text, int limit, Lang lang, CancellationToken ct)
    {
        var words = Text.Normalize(text ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct()
            .Take(8)
            .ToList();
        if (words.Sum(w => w.Length) < 2) return [];

        var search = string.Join(" ", words.Select(w => $"\"{w}\""));
        List<HbePlace> places;
        try
        {
            places = await hbe.Places
                .Find(Builders<HbePlace>.Filter.Text(search), new FindOptions { MaxTime = TimeSpan.FromMilliseconds(O.SuggestTimeoutMs) })
                .Sort(new BsonDocument { { "country_rank", -1 }, { "type_rank", 1 }, { "region_type_rank", -1 } })
                .Limit(limit * 3)
                .ToListAsync(ct);
        }
        catch (MongoExecutionTimeoutException)
        {
            logger.LogWarning("Tìm địa điểm \"{Query}\" quá {Timeout} ms", text, O.SuggestTimeoutMs);
            return [];
        }

        return places
            .Select(p => p.Type switch
            {
                "Region" when !string.IsNullOrEmpty(p.RegionId) => new PlaceSuggestionDto(
                    "region", p.RegionId, lang.Pick(p.ViName, p.EnName),
                    HbeMapping.NullIfEmpty(lang.Pick(p.AncestorViName, p.AncestorEnName)), p.RegionType, p.CountryCode),
                "Property" when !string.IsNullOrEmpty(p.PropertyId) => new PlaceSuggestionDto(
                    "hotel", p.PropertyId, lang.Pick(p.ViName, p.EnName),
                    HbeMapping.NullIfEmpty(lang.Pick(p.AncestorViName, p.AncestorEnName)), null, p.CountryCode),
                _ => null,
            })
            .OfType<PlaceSuggestionDto>()
            .Where(s => s.Name.Length > 0)
            .DistinctBy(s => (s.Type, s.Id))
            .Take(limit)
            .ToList();
    }

    // ---------- Khách sạn ----------

    private static BsonDocument Convert(string path, string to) => new("$convert", new BsonDocument
    {
        { "input", path }, { "to", to }, { "onError", 0 }, { "onNull", 0 },
    });

    /// <summary>Danh sách key của một object con (vd. amenities), an toàn khi trường thiếu/null/không phải object.</summary>
    private static BsonDocument KeysOf(string path) => new("$map", new BsonDocument
    {
        { "input", new BsonDocument("$objectToArray", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", path), "object" }),
                path,
                new BsonDocument(),
            })) },
        { "in", "$$this.k" },
    });

    /// <summary>Tóm tắt khách sạn (chạy trên MongoDB 4.4+): chỉ trả về vài trường thay vì cả document.</summary>
    private static readonly BsonDocument RowProjection = new("$project", new BsonDocument
    {
        { "_id", "$external_property_id" },
        { "name", "$name" },
        { "category", "$category.name" },
        { "city", "$address.city" },
        { "line1", "$address.line_1" },
        { "countryCode", "$address.country_code" },
        { "stars", Convert("$ratings.property.rating", "double") },
        { "overall", Convert("$ratings.guest.overall", "double") },
        { "reviewCount", Convert("$ratings.guest.count", "int") },
        {
            "image", new BsonDocument("$let", new BsonDocument
            {
                { "vars", new BsonDocument("imgs", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$isArray", "$images"), "$images", new BsonArray(),
                    })) },
                { "in", new BsonDocument("$let", new BsonDocument
                    {
                        { "vars", new BsonDocument
                            {
                                { "hero", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$filter", new BsonDocument
                                        {
                                            { "input", "$$imgs" },
                                            { "cond", new BsonDocument("$eq", new BsonArray { "$$this.hero_image", true }) },
                                        }),
                                        0,
                                    }) },
                                { "first", new BsonDocument("$arrayElemAt", new BsonArray { "$$imgs", 0 }) },
                            } },
                        // MongoDB 4.4: $ifNull chỉ nhận 2 tham số.
                        { "in", new BsonDocument("$ifNull", new BsonArray
                            {
                                "$$hero.links.1000px.href",
                                new BsonDocument("$ifNull", new BsonArray { "$$first.links.1000px.href", "$$first.links.350px.href" }),
                            }) },
                    }) },
            })
        },
        { "lat", "$location.coordinates.latitude" },
        { "lng", "$location.coordinates.longitude" },
        { "amenityIds", KeysOf("$amenities") },
        { "petIds", KeysOf("$attributes.pets") },
    });

    private async Task<List<HbePropertyRow>> LoadRowsAsync(Lang lang, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("external_property_id", new BsonDocument("$in", new BsonArray(ids)))),
            RowProjection,
        };
        return await hbe.Properties(lang).Aggregate<HbePropertyRow>(pipeline, cancellationToken: ct).ToListAsync(ct);
    }

    /// <summary>Tóm tắt mọi khách sạn của vùng (giữ trong bộ nhớ <see cref="HbeOptions.CacheMinutes"/> phút).</summary>
    public Task<IReadOnlyList<HbePropertyRow>> GetRegionRowsAsync(string regionId, Lang lang, CancellationToken ct) =>
        CachedAsync<IReadOnlyList<HbePropertyRow>>($"rows:{lang}:{regionId}", async () =>
        {
            var region = await hbe.Regions
                .Find(r => r.RegionId == regionId)
                .Project<HbeRegion>(Builders<HbeRegion>.Projection
                    .Include(r => r.RegionId).Include(r => r.PropertyIds).Include(r => r.ExpandedPropertyIds))
                .FirstOrDefaultAsync()
                ?? throw ApiException.NotFound("Không tìm thấy địa điểm.");
            // Vùng nhỏ (điểm tham quan) có thể chỉ có danh sách "lân cận".
            var ids = region.PropertyIds is { Count: > 0 } ? region.PropertyIds : region.ExpandedPropertyIds ?? [];
            var started = DateTime.UtcNow;
            var rows = await LoadRowsAsync(lang, ids.Distinct().ToList(), CancellationToken.None);
            logger.LogInformation("Nạp {Count} khách sạn của vùng {Region} ({Lang}) trong {Ms} ms",
                rows.Count, regionId, lang, (int)(DateTime.UtcNow - started).TotalMilliseconds);
            return rows;
        }, ct);

    public async Task<SearchResultDto> SearchAsync(SearchQuery q, CancellationToken ct)
    {
        var v = new Validation();
        var lang = Langs.Parse(q.Lang, v);
        var sort = string.IsNullOrWhiteSpace(q.Sort) ? "recommended" : q.Sort.Trim().ToLowerInvariant();
        var amenities = (q.Amenities ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).Distinct().ToList();
        var page = q.Page ?? 1;
        var pageSize = q.PageSize ?? 20;
        var text = q.Q?.Trim() ?? "";
        var regionId = q.RegionId?.Trim();

        v.Check(!string.IsNullOrEmpty(regionId) || text.Length >= 2, "q", "Nhập điểm đến (ít nhất 2 ký tự) hoặc chọn một địa điểm.")
         .Check(Sorts.Contains(sort), "sort", $"Giá trị hợp lệ: {string.Join(", ", Sorts)}.")
         .Check(amenities.All(AmenityMap.IsKnown), "amenities", $"Tiện nghi hợp lệ: {string.Join(", ", AmenityMap.Keys)}.")
         .Check(q.MinStars is null or (>= 0 and <= 5), "minStars", "Hạng sao từ 0 đến 5.")
         .Check(q.MinRating is null or (>= 0 and <= 10), "minRating", "Điểm đánh giá từ 0 đến 10.")
         .Check(page >= 1, "page", "Trang bắt đầu từ 1.")
         .Check(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"pageSize từ 1 đến {MaxPageSize}.");
        v.ThrowIfInvalid();

        RegionDto? region = null;
        IReadOnlyList<HbePropertyRow> rows;
        if (!string.IsNullOrEmpty(regionId))
        {
            region = await GetRegionAsync(regionId, lang, ct);
            rows = await GetRegionRowsAsync(regionId, lang, ct);
        }
        else
        {
            // Từ khóa: lấy vùng khớp nhất; nếu chỉ khớp tên khách sạn thì liệt kê các khách sạn đó.
            var matches = await SuggestCoreAsync(text, MaxSuggestLimit, lang, ct);
            var best = matches.FirstOrDefault(m => m.Type == "region");
            if (best is not null)
            {
                region = await GetRegionAsync(best.Id, lang, ct);
                rows = await GetRegionRowsAsync(best.Id, lang, ct);
            }
            else
            {
                var ids = matches.Where(m => m.Type == "hotel").Select(m => m.Id).ToList();
                var byId = (await LoadRowsAsync(lang, ids, ct)).ToDictionary(r => r.Id);
                rows = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(); // giữ thứ tự liên quan
            }
        }

        var filtered = rows.Where(r =>
            (q.MinStars is not { } s || r.Stars >= s) &&
            (q.MinRating is not { } mr || HbeMapping.ToTen(r.Overall) >= mr) &&
            (amenities.Count == 0 || amenities.All(a => AmenityMap.Has(a, r.AmenityIds, r.PetIds))));

        var byName = StringComparer.Create(CultureInfo.GetCultureInfo(lang == Lang.En ? "en-US" : "vi-VN"), true);
        IEnumerable<HbePropertyRow> sorted = sort switch
        {
            "rating" => filtered.OrderByDescending(r => r.Overall).ThenByDescending(r => r.ReviewCount),
            "stars" => filtered.OrderByDescending(r => r.Stars).ThenByDescending(r => r.Overall),
            "name" => filtered.OrderBy(r => r.Name ?? "", byName),
            // Khớp tên khách sạn: giữ thứ tự liên quan của tìm kiếm.
            _ when region is null => filtered,
            // "Đề xuất": điểm đánh giá + số lượt đánh giá, khách sạn có ảnh trước.
            _ => filtered
                .OrderByDescending(r => !string.IsNullOrEmpty(r.Image))
                .ThenByDescending(r => HbeMapping.ToTen(r.Overall) + Math.Log10(Math.Max(1, r.ReviewCount)))
                .ThenByDescending(r => r.Stars),
        };
        var all = sorted.ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.ToSummary()).ToList();
        return new SearchResultDto(items, all.Count, page, pageSize, region);
    }

    /// <summary>
    /// Khách sạn được đánh giá cao ở các điểm đến phổ biến đầu tiên, xen kẽ giữa các điểm đến
    /// để danh sách không bị một thành phố chiếm hết.
    /// </summary>
    public async Task<IReadOnlyList<HotelSummaryDto>> GetFeaturedAsync(int limit, Lang lang, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 20);
        var perRegion = new List<Queue<HbePropertyRow>>();
        foreach (var d in O.Destinations.DistinctBy(d => d.RegionId).Take(Math.Max(1, O.FeaturedFromDestinations)))
        {
            try
            {
                var rows = await GetRegionRowsAsync(d.RegionId, lang, ct);
                perRegion.Add(new Queue<HbePropertyRow>(rows
                    .Where(r => r.ReviewCount >= O.FeaturedMinReviews && !string.IsNullOrEmpty(r.Image))
                    .OrderByDescending(r => r.Overall).ThenByDescending(r => r.ReviewCount)));
            }
            catch (ApiException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                logger.LogWarning("Điểm đến {Region} không có trong epa_regions", d.RegionId);
            }
        }

        var result = new List<HotelSummaryDto>();
        var seen = new HashSet<string>();
        while (result.Count < limit && perRegion.Any(qu => qu.Count > 0))
        {
            foreach (var queue in perRegion)
            {
                while (queue.TryDequeue(out var row))
                {
                    if (!seen.Add(row.Id)) continue;
                    result.Add(row.ToSummary());
                    break;
                }
                if (result.Count == limit) break;
            }
        }
        return result;
    }

    public async Task<HbeProperty> GetPropertyAsync(string id, Lang lang, CancellationToken ct) =>
        await hbe.Properties(lang).Find(p => p.PropertyId == id).FirstOrDefaultAsync(ct)
        ?? throw ApiException.NotFound("Không tìm thấy khách sạn.");

    public async Task<HotelDetailDto> GetDetailAsync(string id, Lang lang, CancellationToken ct) =>
        (await GetPropertyAsync(id, lang, ct)).ToDetail(lang);

    public async Task EnsureHotelExistsAsync(string id, CancellationToken ct)
    {
        var exists = await hbe.ViProperties.Find(p => p.PropertyId == id).Limit(1).CountDocumentsAsync(ct) > 0;
        if (!exists) throw ApiException.NotFound("Không tìm thấy khách sạn.");
    }

    /// <summary>Tóm tắt các khách sạn theo id, giữ thứ tự đầu vào; id không tồn tại bị bỏ qua.</summary>
    public async Task<IReadOnlyList<HotelSummaryDto>> GetByIdsAsync(IReadOnlyCollection<string> ids, Lang lang, CancellationToken ct)
    {
        var byId = (await LoadRowsAsync(lang, ids.Distinct().ToList(), ct)).ToDictionary(r => r.Id);
        return ids.Where(byId.ContainsKey).Distinct().Select(id => byId[id].ToSummary()).ToList();
    }

    // ---------- Cache ----------

    /// <summary>
    /// Cache theo key; các request đồng thời dùng chung một lần nạp. Việc nạp không gắn với request đang chờ
    /// (request hủy không làm hỏng lần nạp của người khác); lỗi thì không cache.
    /// </summary>
    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load, CancellationToken ct)
    {
        var task = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return Task.Run(load, CancellationToken.None);
        })!;
        try
        {
            return await task.WaitAsync(ct);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            cache.Remove(key);
            throw;
        }
    }
}

/// <summary>Nạp sẵn (chạy nền) danh sách khách sạn của các điểm đến phổ biến để trang chủ mở nhanh.</summary>
public sealed class CatalogWarmUp(CatalogService catalog, IOptions<HbeOptions> options, ILogger<CatalogWarmUp> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WarmUp) return;
        try
        {
            await catalog.GetDestinationsAsync(Lang.Vi, stoppingToken);
            await catalog.GetFeaturedAsync(6, Lang.Vi, stoppingToken);
            foreach (var d in options.Value.Destinations)
            {
                await catalog.GetRegionRowsAsync(d.RegionId, Lang.Vi, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không nạp sẵn được dữ liệu điểm đến từ hbe");
        }
    }
}
