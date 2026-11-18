using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VietStay.Api.Contracts;
using VietStay.Api.Domain;
using static VietStay.Api.Tests.HbeTestData;

namespace VietStay.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    private Task<T?> Get<T>(string url) => _client.GetFromJsonAsync<T>(url, ApiFactory.Json);

    [Fact]
    public async Task Countries_skip_missing_codes_and_follow_language()
    {
        var vi = (await Get<List<CountryDto>>("/api/countries"))!;
        Assert.Equal(["Nhật Bản", "Việt Nam"], vi.Select(c => c.Name));
        var en = (await Get<List<CountryDto>>("/api/countries?lang=en"))!;
        Assert.Equal(["Japan", "Vietnam"], en.Select(c => c.Name));
    }

    [Fact]
    public async Task Destinations_come_from_config_in_order_and_skip_unknown_regions()
    {
        var d = (await Get<List<DestinationDto>>("/api/destinations"))!;
        Assert.Equal([DaNang, HoiAn], d.Select(x => x.Id));
        Assert.Equal("Đà Nẵng", d[0].Name);
        Assert.Equal(3, d[0].HotelCount);
        Assert.Equal("https://img.test/danang.jpg", d[0].Image);
    }

    [Theory]
    [InlineData("da nang", "region", DaNang)]
    [InlineData("Đà Nẵng", "region", DaNang)]
    [InlineData("ocean pearl", "hotel", Ocean)]
    [InlineData("my khe", "region", MyKhe)]
    public async Task Suggest_matches_words_without_diacritics(string q, string type, string id)
    {
        var s = (await Get<List<PlaceSuggestionDto>>($"/api/places/suggest?q={Uri.EscapeDataString(q)}"))!;
        Assert.Equal((type, id), (s[0].Type, s[0].Id));
    }

    [Fact]
    public async Task Suggest_lists_regions_before_hotels_and_normalizes_unicode()
    {
        var s = (await Get<List<PlaceSuggestionDto>>("/api/places/suggest?q=da%20nang"))!;
        Assert.Equal(["region", "hotel"], s.Select(x => x.Type).Distinct());
        Assert.Equal("Việt Nam", s[0].Subtitle); // DB lưu NFD
        Assert.Empty((await Get<List<PlaceSuggestionDto>>("/api/places/suggest?q=d"))!);
        Assert.Empty((await Get<List<PlaceSuggestionDto>>("/api/places/suggest?q=khongcodiadiemnay"))!);
    }

    [Fact]
    public async Task Region_detail_counts_hotels()
    {
        var r = (await Get<RegionDto>($"/api/regions/{DaNang}?lang=en"))!;
        Assert.Equal(("Da Nang", "city", 3), (r.Name, r.Type, r.HotelCount));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/regions/khong-co")).StatusCode);
    }

    [Fact]
    public async Task Search_by_region_ranks_rated_hotels_with_images_first()
    {
        var r = (await Get<SearchResultDto>($"/api/hotels/search?regionId={DaNang}"))!;
        Assert.Equal(3, r.Total);
        Assert.Equal([Ocean, River, Hostel], r.Items.Select(h => h.Id));
        Assert.Equal(DaNang, r.Region!.Id);

        var ocean = r.Items[0];
        Assert.Equal((5.0, 9.2, 812), (ocean.Stars, ocean.Rating, ocean.ReviewCount)); // 4.6/5 → 9.2/10
        Assert.Equal("https://img.test/1001-hero_z.jpg", ocean.Image);                    // ảnh hero, không phải ảnh đầu
        Assert.Equal(["wifi", "pool", "spa", "restaurant"], ocean.Amenities);
        Assert.Equal("1001 Võ Nguyên Giáp, Đà Nẵng", ocean.Address);

        var hostel = r.Items[2];
        Assert.Equal((0.0, 0.0, ""), (hostel.Stars, hostel.Rating, hostel.Image));
    }

    [Fact]
    public async Task Search_applies_filters_sort_and_paging()
    {
        var pets = (await Get<SearchResultDto>($"/api/hotels/search?regionId={DaNang}&amenities=pet"))!;
        Assert.Equal([River], pets.Items.Select(h => h.Id));

        var filtered = (await Get<SearchResultDto>($"/api/hotels/search?regionId={DaNang}&minStars=4&minRating=9&amenities=pool,spa"))!;
        Assert.Equal([Ocean], filtered.Items.Select(h => h.Id));

        var byName = (await Get<SearchResultDto>($"/api/hotels/search?regionId={DaNang}&sort=name&pageSize=2&page=2"))!;
        Assert.Equal((3, 2, 2), (byName.Total, byName.Page, byName.PageSize));
        Assert.Equal([River], byName.Items.Select(h => h.Id)); // Hostel Biển Xanh, Ocean Pearl | River View
    }

    [Fact]
    public async Task Search_uses_expanded_ids_for_small_regions()
    {
        var r = (await Get<SearchResultDto>($"/api/hotels/search?regionId={MyKhe}"))!;
        Assert.Equal([Ocean], r.Items.Select(h => h.Id));
    }

    [Fact]
    public async Task Search_by_keyword_resolves_region_or_hotel_names()
    {
        var region = (await Get<SearchResultDto>("/api/hotels/search?q=hoi%20an"))!;
        Assert.Equal(HoiAn, region.Region!.Id);
        Assert.Equal([Lantern], region.Items.Select(h => h.Id));

        var hotel = (await Get<SearchResultDto>("/api/hotels/search?q=ocean%20pearl&lang=en"))!;
        Assert.Null(hotel.Region);
        Assert.Equal("Ocean Pearl Da Nang", Assert.Single(hotel.Items).Name);
    }

    [Theory]
    [InlineData("", "q")]
    [InlineData("q=da%20nang&sort=price-asc", "sort")]
    [InlineData("q=da%20nang&amenities=wifi,sauna", "amenities")]
    [InlineData("q=da%20nang&minStars=6", "minStars")]
    [InlineData("q=da%20nang&pageSize=500", "pageSize")]
    [InlineData("q=da%20nang&lang=fr", "lang")]
    public async Task Search_rejects_invalid_input(string query, string field)
    {
        var res = await _client.GetAsync($"/api/hotels/search?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Featured_needs_enough_reviews_and_alternates_destinations()
    {
        var f = (await Get<List<HotelSummaryDto>>("/api/hotels/featured?limit=5"))!;
        Assert.Equal([Ocean, Lantern, River], f.Select(h => h.Id)); // Hostel: chưa có đánh giá
    }

    [Fact]
    public async Task Hotel_detail_converts_html_and_rooms()
    {
        var h = (await Get<HotelDetailDto>($"/api/hotels/{Ocean}"))!;
        Assert.Equal("Ocean Pearl Đà Nẵng", h.Name);
        Assert.Equal("Cách biển 2 phút\nđi bộ.", h.Description);
        Assert.Equal("Gần biển Mỹ Khê", h.Headline);
        Assert.Equal(["rooms", "attractions"], h.Sections.Select(s => s.Key));
        Assert.Equal("Cầu Rồng - 3 km\nChợ Hàn - 4 km", h.Sections[1].Text);
        Assert.Equal("https://img.test/1001-hero_z.jpg", h.Images[0].Url);
        Assert.Equal("https://img.test/1001-hero_b.jpg", h.Images[0].Thumb);
        Assert.Contains(h.Amenities, a => a.Name == "Số lượng nhà hàng: 2");
        Assert.Equal(9.6, h.RatingBreakdown!.Cleanliness);
        Assert.Null(h.RatingBreakdown.Comfort);
        Assert.Equal("• Giấy tờ tùy thân\n• Đặt cọc & thẻ tín dụng", h.Policies.CheckInInstructions);
        Assert.Equal("Lễ tân 24 giờ.", h.Policies.SpecialInstructions);
        Assert.Equal(["Không có thang máy"], h.Policies.General);

        var deluxe = h.Rooms.Single(r => r.Id == "r-deluxe");
        Assert.Equal(("Phòng Deluxe, hướng biển", 32.0, 2), (deluxe.Name, deluxe.SizeM2, deluxe.MaxGuests));
        Assert.Equal("1 giường King\nPhòng 32 m²", deluxe.Description);
        Assert.Equal(["1 giường King"], deluxe.Beds);
        Assert.Equal(["Hướng biển"], deluxe.Views);
        Assert.Null(h.Rooms.Single(r => r.Id == "r-family").SizeM2);

        var en = (await Get<HotelDetailDto>($"/api/hotels/{Ocean}?lang=en"))!;
        Assert.Equal(("Ocean Pearl Da Nang", "Rooms"), (en.Name, en.Sections[0].Title));
    }

    [Fact]
    public async Task Unknown_hotel_returns_404_problem()
    {
        var res = await _client.GetAsync("/api/hotels/khong-ton-tai");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task References_filter_by_type_and_ids()
    {
        var all = (await Get<List<ReferenceDto>>("/api/references?type=amenities_property"))!;
        Assert.Equal(3, all.Count);
        var some = (await Get<List<ReferenceDto>>("/api/references?type=amenities_property&ids=2390&lang=en"))!;
        Assert.Equal("Free WiFi", Assert.Single(some).Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/references?type=khong-co")).StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:8082", true)]   // Expo đổi cổng khi 8081 bận
    [InlineData("http://127.0.0.1:19006", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Development_cors_allows_any_localhost_port(string origin, bool allowed)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/countries");
        req.Headers.Add("Origin", origin);
        var res = await _client.SendAsync(req);
        Assert.Equal(allowed, res.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) && v.Single() == origin);
    }

    [Fact]
    public async Task Health_reports_mongo_ok()
    {
        Assert.Equal("Healthy", await _client.GetStringAsync("/health"));
    }
}

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_login_and_profile()
    {
        var client = api.CreateClient();
        var email = $"An{Guid.NewGuid():N}@Example.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Trần An", email, "0987 654 321", "matkhau123"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        var dup = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Trần An", email.ToLowerInvariant(), "0987654321", "matkhau123"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "sai-mat-khau"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email.ToUpperInvariant(), "matkhau123"));
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;
        Assert.Equal(email.ToLowerInvariant(), auth.User.Email);
        Assert.Equal("0987654321", auth.User.Phone);

        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.Token);
        var updated = await client.PutAsJsonAsync("/api/me", new UpdateProfileRequest("Trần Bình An", null));
        Assert.Equal("Trần Bình An", (await updated.Content.ReadFromJsonAsync<UserDto>())!.Name);
    }

    [Fact]
    public async Task Register_validates_input()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("A", "khong-phai-email", "123", "ngan"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var field in new[] { "name", "email", "phone", "password" }) Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Protected_endpoints_require_token()
    {
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Favorites_add_list_remove()
    {
        var client = await api.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/me/favorites/{River}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/me/favorites/{Lantern}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/me/favorites/{Lantern}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/me/favorites/khong-co", null)).StatusCode);

        var favs = await client.GetFromJsonAsync<List<HotelSummaryDto>>("/api/me/favorites", ApiFactory.Json);
        Assert.Equal([Lantern, River], favs!.Select(h => h.Id)); // mới thêm trước

        await client.DeleteAsync($"/api/me/favorites/{Lantern}");
        await client.DeleteAsync($"/api/me/favorites/{River}");
        Assert.Empty((await client.GetFromJsonAsync<List<HotelSummaryDto>>("/api/me/favorites", ApiFactory.Json))!);
    }
}

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory api)
{
    private static DateOnly Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset);

    private static CreateBookingRequest Request(string roomId, DateOnly checkIn, int nights, int guests = 2, int rooms = 1) =>
        new(Ocean, roomId, checkIn, checkIn.AddDays(nights), guests, rooms,
            "Nguyễn Văn Test", "test@example.com", "0912 345 678", "Phòng tầng cao", PaymentMethod.BankTransfer);

    [Fact]
    public async Task Create_list_get_and_cancel_booking_request()
    {
        var client = await api.CreateUserClientAsync();
        var res = await client.PostAsJsonAsync("/api/bookings", Request("r-deluxe", Day(40), 2), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var booking = (await res.Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;

        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Contains("\"pending\"", await res.Content.ReadAsStringAsync());
        Assert.Null(booking.Total); // hbe không có giá
        Assert.Equal(("Ocean Pearl Đà Nẵng", "Phòng Deluxe, hướng biển", 2), (booking.HotelName, booking.RoomName, booking.Nights));
        Assert.Equal("https://img.test/1001-hero_z.jpg", booking.HotelImage);
        Assert.Matches("^VS[A-Z0-9]{6}$", booking.Code);
        Assert.Equal("0912345678", booking.GuestPhone);
        Assert.True(booking.CanCancel);

        var list = await client.GetFromJsonAsync<List<BookingDto>>("/api/bookings", ApiFactory.Json);
        Assert.Equal(booking.Id, Assert.Single(list!).Id);
        Assert.Equal(booking.Code, (await client.GetFromJsonAsync<BookingDto>($"/api/bookings/{booking.Id}", ApiFactory.Json))!.Code);

        var cancelled = (await (await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null))
            .Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.CanCancel);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Rejects_unknown_room_and_too_many_guests()
    {
        var client = await api.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/bookings", Request("khong-co", Day(30), 1), ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/bookings", Request("r-deluxe", Day(30), 1, guests: 3), ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/bookings", Request("r-deluxe", Day(30), 1, guests: 4, rooms: 2), ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Rejects_past_dates()
    {
        var client = await api.CreateUserClientAsync();
        var res = await client.PostAsJsonAsync("/api/bookings", Request("r-family", Day(-1), 2), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_cancel_others_bookings()
    {
        var owner = await api.CreateUserClientAsync();
        var other = await api.CreateUserClientAsync();
        var res = await owner.PostAsJsonAsync("/api/bookings", Request("r-family", Day(20), 1), ApiFactory.Json);
        var booking = (await res.Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/bookings/{booking.Id}/cancel", null)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<BookingDto>>("/api/bookings", ApiFactory.Json))!);
    }
}
