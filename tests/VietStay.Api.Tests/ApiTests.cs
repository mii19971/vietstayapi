using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VietStay.Api.Contracts;
using VietStay.Api.Domain;

namespace VietStay.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task Cities_are_seeded_in_order()
    {
        var cities = await _client.GetFromJsonAsync<List<CityDto>>("/api/cities", ApiFactory.Json);
        Assert.Equal(14, cities!.Count);
        Assert.Equal("Hà Nội", cities[0].Name);
    }

    [Fact]
    public async Task Search_ignores_vietnamese_diacritics()
    {
        var result = await _client.GetFromJsonAsync<SearchResultDto>("/api/hotels/search?q=da%20nang", ApiFactory.Json);
        Assert.Equal(2, result!.Total);
        Assert.All(result.Items, h => Assert.Equal("Đà Nẵng", h.City));
    }

    [Fact]
    public async Task Search_applies_filters_and_sort()
    {
        var r = await _client.GetFromJsonAsync<SearchResultDto>(
            "/api/hotels/search?minStars=5&amenities=beach,spa&sort=price-asc", ApiFactory.Json);
        Assert.Equal(["hl-bay-pearl", "dn-my-khe-ocean", "pq-sunset-sanctuary"], r!.Items.Select(h => h.Id));
        Assert.True(r.Items[0].MinPrice <= r.Items[1].MinPrice && r.Items[1].MinPrice <= r.Items[2].MinPrice);
    }

    [Fact]
    public async Task Suggest_returns_nearby_hotels_sorted_by_price()
    {
        var r = await _client.GetFromJsonAsync<SuggestResultDto>("/api/hotels/suggest?q=da%20nang", ApiFactory.Json);
        Assert.Equal("city", r!.Place!.Type);
        Assert.Equal("da-nang", r.Place.Id);
        // Bán kính mặc định 20 km: chỉ hai khách sạn Đà Nẵng; Hội An (~24 km) nằm ngoài.
        Assert.Equal(20, r.RadiusKm);
        Assert.Equal(["dn-han-river-view", "dn-my-khe-ocean"], r.Items.Select(i => i.Hotel.Id));
        Assert.Equal(r.Items.Select(i => i.Hotel.MinPrice).Order(), r.Items.Select(i => i.Hotel.MinPrice));
        Assert.All(r.Items, i => Assert.InRange(i.DistanceKm, 0, 20));
    }

    [Fact]
    public async Task Suggest_radius_can_be_widened()
    {
        // radiusKm=50 → thêm Lantern Garden (Hội An, ~24 km), vẫn sắp theo giá.
        var r = await _client.GetFromJsonAsync<SuggestResultDto>("/api/hotels/suggest?q=da%20nang&radiusKm=50", ApiFactory.Json);
        Assert.Equal(["dn-han-river-view", "ha-lantern-garden", "dn-my-khe-ocean"], r!.Items.Select(i => i.Hotel.Id));
    }

    [Fact]
    public async Task Suggest_limit_keeps_the_nearest_hotels()
    {
        // limit=1 → chỉ khách sạn gần trung tâm Đà Nẵng nhất (Han River ~2 km), dù không rẻ nhất vùng.
        var r = await _client.GetFromJsonAsync<SuggestResultDto>("/api/hotels/suggest?q=da%20nang&limit=1", ApiFactory.Json);
        Assert.Equal("dn-han-river-view", Assert.Single(r!.Items).Hotel.Id);
    }

    [Theory]
    [InlineData("sai gon", "city", "ho-chi-minh", new[] { "hcm-district3-boutique", "hcm-saigon-skyline" })]
    [InlineData("pine hill", "hotel", "dl-pine-hill", new[] { "dl-pine-hill" })]
    [InlineData("khach san hue", "city", "hue", new[] { "hue-imperial-garden", "hue-perfume-river" })]
    public async Task Suggest_understands_aliases_and_hotel_names(string q, string type, string placeId, string[] expected)
    {
        var r = await _client.GetFromJsonAsync<SuggestResultDto>($"/api/hotels/suggest?q={Uri.EscapeDataString(q)}", ApiFactory.Json);
        Assert.Equal(type, r!.Place!.Type);
        Assert.Equal(placeId, r.Place.Id);
        Assert.Equal(expected, r.Items.Select(i => i.Hotel.Id));
    }

    [Theory]
    [InlineData("xyz khong co")]
    [InlineData("a")]
    public async Task Suggest_returns_empty_when_place_unknown(string q)
    {
        var r = await _client.GetFromJsonAsync<SuggestResultDto>($"/api/hotels/suggest?q={Uri.EscapeDataString(q)}", ApiFactory.Json);
        Assert.Null(r!.Place);
        Assert.Empty(r.Items);
    }

    [Fact]
    public async Task Search_only_returns_hotels_with_rooms_big_enough()
    {
        // 4 khách / 1 phòng: chỉ phòng Gia đình (tối đa 4) phù hợp → minPrice = giá phòng Gia đình.
        var r = await _client.GetFromJsonAsync<SearchResultDto>("/api/hotels/search?q=pine&guests=4&rooms=1", ApiFactory.Json);
        Assert.Equal(1_220_000, Assert.Single(r!.Items).MinPrice);
    }

    [Theory]
    [InlineData("checkIn=2020-01-01&checkOut=2020-01-02", "checkIn")]
    [InlineData("checkIn=2099-01-05&checkOut=2099-01-05", "checkOut")]
    [InlineData("checkIn=05-01-2099", "checkIn")]
    [InlineData("guests=2&rooms=3", "rooms")]
    [InlineData("sort=cheap", "sort")]
    [InlineData("amenities=sauna", "amenities")]
    public async Task Search_rejects_invalid_input(string query, string field)
    {
        var res = await _client.GetAsync($"/api/hotels/search?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_hotel_returns_404_problem()
    {
        var res = await _client.GetAsync("/api/hotels/khong-ton-tai");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("http://localhost:8082", true)]   // Expo đổi cổng khi 8081 bận
    [InlineData("http://127.0.0.1:19006", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Development_cors_allows_any_localhost_port(string origin, bool allowed)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/cities");
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
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/api/me/favorites/dl-pine-hill", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/api/me/favorites/dl-pine-hill", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/me/favorites/khong-co", null)).StatusCode);

        var favs = await client.GetFromJsonAsync<List<HotelSummaryDto>>("/api/me/favorites", ApiFactory.Json);
        Assert.Equal("dl-pine-hill", Assert.Single(favs!).Id);

        await client.DeleteAsync("/api/me/favorites/dl-pine-hill");
        Assert.Empty((await client.GetFromJsonAsync<List<HotelSummaryDto>>("/api/me/favorites", ApiFactory.Json))!);
    }
}

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory api)
{
    // Mỗi test dùng một khoảng ngày riêng ở tương lai xa để không ảnh hưởng lẫn nhau.
    private static DateOnly Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset);

    private static CreateBookingRequest Request(string roomId, DateOnly checkIn, int nights, int guests = 2, int rooms = 1) =>
        new(roomId[..roomId.LastIndexOf('-')], roomId, checkIn, checkIn.AddDays(nights), guests, rooms,
            "Nguyễn Văn Test", "test@example.com", "0912 345 678", "Phòng tầng cao", PaymentMethod.BankTransfer);

    private static async Task<List<RoomOfferDto>> Offers(HttpClient c, string hotelId, DateOnly checkIn, int nights) =>
        (await c.GetFromJsonAsync<List<RoomOfferDto>>(
            $"/api/hotels/{hotelId}/rooms?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkIn.AddDays(nights):yyyy-MM-dd}", ApiFactory.Json))!;

    [Fact]
    public async Task Create_list_get_and_cancel_booking()
    {
        var client = await api.CreateUserClientAsync();
        var checkIn = Day(40);

        var res = await client.PostAsJsonAsync("/api/bookings", Request("dn-my-khe-ocean-dlx", checkIn, 2), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var booking = (await res.Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;

        // Deluxe = 2.900.000 × 1,4 = 4.060.000 ₫/đêm; 2 đêm = 8.120.000; thuế 10% = 812.000.
        Assert.Equal(4_060_000, booking.PricePerNight);
        Assert.Equal(8_120_000, booking.Subtotal);
        Assert.Equal(812_000, booking.Taxes);
        Assert.Equal(8_932_000, booking.Total);
        Assert.Matches("^VS[A-Z0-9]{6}$", booking.Code);
        Assert.Equal("0912345678", booking.GuestPhone);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.True(booking.CanCancel);
        Assert.Contains("\"bank_transfer\"", await res.Content.ReadAsStringAsync());

        var deluxe = (await Offers(client, "dn-my-khe-ocean", checkIn, 2)).Single(o => o.Room.Id == "dn-my-khe-ocean-dlx");
        Assert.Equal(3, deluxe.Remaining);

        var list = await client.GetFromJsonAsync<List<BookingDto>>("/api/bookings", ApiFactory.Json);
        Assert.Equal(booking.Id, Assert.Single(list!).Id);

        var cancel = await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        var cancelled = (await cancel.Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.CanCancel);

        deluxe = (await Offers(client, "dn-my-khe-ocean", checkIn, 2)).Single(o => o.Room.Id == "dn-my-khe-ocean-dlx");
        Assert.Equal(4, deluxe.Remaining);

        var again = await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Cannot_overbook_last_room()
    {
        var a = await api.CreateUserClientAsync();
        var b = await api.CreateUserClientAsync();
        var checkIn = Day(60);

        // Suite chỉ có 1 phòng. Đơn thứ hai trùng 1 đêm (đêm thứ 3 của đơn đầu) phải bị từ chối.
        Assert.Equal(HttpStatusCode.Created, (await a.PostAsJsonAsync("/api/bookings", Request("hn-west-lake-retreat-ste", checkIn, 3), ApiFactory.Json)).StatusCode);
        var clash = await b.PostAsJsonAsync("/api/bookings", Request("hn-west-lake-retreat-ste", checkIn.AddDays(2), 2), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        // Đơn thất bại không được giữ lại đêm nào: đêm ngay sau đơn đầu vẫn còn trống.
        Assert.Equal(1, (await Offers(b, "hn-west-lake-retreat", checkIn.AddDays(3), 1)).Single(o => o.Room.Id.EndsWith("-ste")).Remaining);
        // Trả phòng đúng ngày nhận của đơn khác thì không trùng.
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsJsonAsync("/api/bookings", Request("hn-west-lake-retreat-ste", checkIn.AddDays(3), 2), ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_bookings_never_exceed_inventory()
    {
        var clients = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.CreateUserClientAsync()));
        var checkIn = Day(80);

        // Phòng Gia đình có 3 phòng; 8 người đặt đồng thời → đúng 3 đơn thành công.
        var results = await Task.WhenAll(clients.Select(c =>
            c.PostAsJsonAsync("/api/bookings", Request("sp-cloud-terrace-fam", checkIn, 2), ApiFactory.Json)));

        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, (await Offers(clients[0], "sp-cloud-terrace", checkIn, 2)).Single(o => o.Room.Id.EndsWith("-fam")).Remaining);
    }

    [Fact]
    public async Task Rejects_too_many_guests_for_room()
    {
        var client = await api.CreateUserClientAsync();
        var res = await client.PostAsJsonAsync("/api/bookings", Request("nt-coral-bay-std", Day(30), 1, guests: 3), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_cancel_others_bookings()
    {
        var owner = await api.CreateUserClientAsync();
        var other = await api.CreateUserClientAsync();
        var res = await owner.PostAsJsonAsync("/api/bookings", Request("ha-lantern-garden-std", Day(20), 1), ApiFactory.Json);
        var booking = (await res.Content.ReadFromJsonAsync<BookingDto>(ApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/bookings/{booking.Id}/cancel", null)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<BookingDto>>("/api/bookings", ApiFactory.Json))!);
    }
}
