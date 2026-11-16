using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VietStay.Api.Contracts;
using VietStay.Api.Data;
using VietStay.Api.Domain;

namespace VietStay.Api.Services;

public sealed class BookingService(
    MongoDb db,
    HotelService hotels,
    InventoryService inventory,
    StayService stays,
    AppClock clock,
    IOptions<BookingOptions> options)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<BookingDto> CreateAsync(string userId, CreateBookingRequest req, CancellationToken ct)
    {
        var v = new Validation();
        v.Check(!string.IsNullOrWhiteSpace(req.HotelId), "hotelId", "Thiếu khách sạn.")
         .Check(!string.IsNullOrWhiteSpace(req.RoomId), "roomId", "Thiếu loại phòng.")
         .Check(req.CheckIn is not null, "checkIn", "Thiếu ngày nhận phòng.")
         .Check(req.CheckOut is not null, "checkOut", "Thiếu ngày trả phòng.")
         .Check((req.GuestName?.Trim().Length ?? 0) is >= 2 and <= 100, "guestName", "Họ tên từ 2 đến 100 ký tự.")
         .Check(req.GuestEmail is not null && Text.IsEmail(req.GuestEmail), "guestEmail", "Email không hợp lệ.")
         .Check(req.GuestPhone is not null && Text.IsPhone(req.GuestPhone), "guestPhone", "Số điện thoại không hợp lệ.")
         .Check((req.Note?.Length ?? 0) <= 500, "note", "Ghi chú tối đa 500 ký tự.")
         .Check(req.PaymentMethod is null || Enum.IsDefined(req.PaymentMethod.Value), "paymentMethod", "Phương thức thanh toán không hợp lệ.");
        Stay? stay = null;
        if (req.CheckIn is { } checkIn && req.CheckOut is { } checkOut)
        {
            stay = stays.Validate(checkIn, checkOut, req.Guests ?? 1, req.Rooms ?? 1, v);
        }
        v.ThrowIfInvalid();

        var hotel = await hotels.GetHotelAsync(req.HotelId!, ct);
        var room = hotel.Rooms.FirstOrDefault(r => r.Id == req.RoomId)
                   ?? throw ApiException.NotFound("Không tìm thấy loại phòng.");
        if (!InventoryService.Fits(room, stay!))
        {
            throw new ApiException(StatusCodes.Status400BadRequest,
                $"{room.Name} chứa tối đa {room.MaxGuests * stay!.Rooms} khách cho {stay.Rooms} phòng.");
        }

        var subtotal = room.Price * stay!.Nights * stay.Rooms;
        var taxes = (long)Math.Round(subtotal * options.Value.TaxRate, MidpointRounding.AwayFromZero);
        var booking = new Booking
        {
            UserId = userId,
            HotelId = hotel.Id,
            HotelName = hotel.Name,
            HotelImage = hotel.Images.FirstOrDefault() ?? "",
            City = hotel.City,
            Address = hotel.Address,
            RoomId = room.Id,
            RoomName = room.Name,
            FreeCancellation = room.FreeCancellation,
            CheckIn = stay.CheckIn,
            CheckOut = stay.CheckOut,
            Nights = stay.Nights,
            Guests = stay.Guests,
            Rooms = stay.Rooms,
            PricePerNight = room.Price,
            Subtotal = subtotal,
            Taxes = taxes,
            Total = subtotal + taxes,
            GuestName = req.GuestName!.Trim(),
            GuestEmail = req.GuestEmail!.Trim(),
            GuestPhone = Text.CleanPhone(req.GuestPhone!),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            PaymentMethod = req.PaymentMethod ?? PaymentMethod.PayAtHotel,
            Status = BookingStatus.Confirmed,
            CreatedAt = clock.UtcNow,
        };

        await inventory.ReserveAsync(hotel, room, stay, ct);
        try
        {
            await InsertWithUniqueCodeAsync(booking, ct);
        }
        catch
        {
            await inventory.ReleaseAsync(room.Id, stay, stay.Rooms);
            throw;
        }
        return booking.ToDto(clock.Today);
    }

    public async Task<IReadOnlyList<BookingDto>> ListAsync(string userId, CancellationToken ct)
    {
        var list = await db.Bookings.Find(b => b.UserId == userId).SortByDescending(b => b.CreatedAt).ToListAsync(ct);
        var today = clock.Today;
        return list.Select(b => b.ToDto(today)).ToList();
    }

    public async Task<BookingDto> GetAsync(string userId, string id, CancellationToken ct) =>
        (await FindOwnedAsync(userId, id, ct)).ToDto(clock.Today);

    public async Task<BookingDto> CancelAsync(string userId, string id, CancellationToken ct)
    {
        var existing = await FindOwnedAsync(userId, id, ct);
        var today = clock.Today;
        if (existing.Status == BookingStatus.Cancelled) throw ApiException.Conflict("Đơn đặt phòng đã được hủy trước đó.");
        if (existing.CheckIn <= today) throw ApiException.Conflict("Không thể hủy khi đã đến ngày nhận phòng.");

        // Cập nhật có điều kiện để hai request hủy đồng thời không hoàn phòng hai lần.
        var cancelled = await db.Bookings.FindOneAndUpdateAsync(
            b => b.Id == existing.Id && b.Status == BookingStatus.Confirmed,
            Builders<Booking>.Update.Set(b => b.Status, BookingStatus.Cancelled).Set(b => b.CancelledAt, clock.UtcNow),
            new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw ApiException.Conflict("Đơn đặt phòng đã được hủy trước đó.");

        await inventory.ReleaseAsync(cancelled.RoomId,
            new Stay(cancelled.CheckIn, cancelled.CheckOut, cancelled.Guests, cancelled.Rooms), cancelled.Rooms);
        return cancelled.ToDto(today);
    }

    private async Task<Booking> FindOwnedAsync(string userId, string id, CancellationToken ct)
    {
        // Đơn của người khác cũng trả 404 để không lộ sự tồn tại của đơn.
        if (!MongoDB.Bson.ObjectId.TryParse(id, out _)) throw ApiException.NotFound("Không tìm thấy đơn đặt phòng.");
        return await db.Bookings.Find(b => b.Id == id && b.UserId == userId).FirstOrDefaultAsync(ct)
               ?? throw ApiException.NotFound("Không tìm thấy đơn đặt phòng.");
    }

    private async Task InsertWithUniqueCodeAsync(Booking booking, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            booking.Code = "VS" + RandomNumberGenerator.GetString(CodeChars, 6);
            try
            {
                await db.Bookings.InsertOneAsync(booking, cancellationToken: ct);
                return;
            }
            catch (Exception ex) when (MongoDb.IsDuplicateKey(ex) && attempt < 5)
            {
                // Trùng mã đặt phòng (rất hiếm) — thử mã khác.
            }
        }
    }
}
