using Microsoft.Extensions.Options;

namespace VietStay.Api.Services;

/// <summary>Khoảng ngày ở + số khách/phòng đã được kiểm tra hợp lệ.</summary>
public sealed record Stay(DateOnly CheckIn, DateOnly CheckOut, int Guests, int Rooms)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
}

public sealed class StayService(AppClock clock, IOptions<BookingOptions> options)
{
    private readonly BookingOptions _o = options.Value;

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
}
