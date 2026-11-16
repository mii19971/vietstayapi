using System.Globalization;
using Microsoft.Extensions.Options;
using VietStay.Api.Contracts;

namespace VietStay.Api.Services;

/// <summary>Khoảng ngày ở + số khách/phòng đã được kiểm tra hợp lệ.</summary>
public sealed record Stay(DateOnly CheckIn, DateOnly CheckOut, int Guests, int Rooms)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = CheckIn; d < CheckOut; d = d.AddDays(1)) yield return d;
    }

    public StayDto ToDto() => new(CheckIn, CheckOut, Nights, Guests, Rooms);
}

public sealed class StayService(AppClock clock, IOptions<BookingOptions> options)
{
    private readonly BookingOptions _o = options.Value;

    /// <summary>Đọc từ query string; thiếu giá trị thì dùng mặc định (nhận phòng ngày mai, 1 đêm, 2 khách, 1 phòng).</summary>
    public Stay FromQuery(string? checkIn, string? checkOut, int? guests, int? rooms, Validation v)
    {
        var inDate = ParseDate(checkIn, "checkIn", v) ?? clock.Today.AddDays(1);
        var outDate = ParseDate(checkOut, "checkOut", v) ?? inDate.AddDays(1);
        return Validate(inDate, outDate, guests ?? 2, rooms ?? 1, v);
    }

    public Stay FromQuery(StayQuery q, Validation v) => FromQuery(q.CheckIn, q.CheckOut, q.Guests, q.Rooms, v);

    public Stay Validate(DateOnly checkIn, DateOnly checkOut, int guests, int rooms, Validation v)
    {
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        v.Check(checkIn >= clock.Today, "checkIn", "Ngày nhận phòng không được ở quá khứ.")
         .Check(nights >= 1, "checkOut", "Ngày trả phòng phải sau ngày nhận phòng.")
         .Check(nights <= _o.MaxNights, "checkOut", $"Chỉ đặt tối đa {_o.MaxNights} đêm.")
         .Check(guests >= 1 && guests <= _o.MaxGuests, "guests", $"Số khách từ 1 đến {_o.MaxGuests}.")
         .Check(rooms >= 1 && rooms <= _o.MaxRooms, "rooms", $"Số phòng từ 1 đến {_o.MaxRooms}.")
         .Check(rooms <= guests, "rooms", "Số phòng không được nhiều hơn số khách.");
        return new Stay(checkIn, checkOut, guests, rooms);
    }

    private static DateOnly? ParseDate(string? s, string field, Validation v)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        v.Check(false, field, "Ngày phải có định dạng yyyy-MM-dd.");
        return null;
    }
}
