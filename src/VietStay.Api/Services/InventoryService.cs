using MongoDB.Driver;
using VietStay.Api.Data;
using VietStay.Api.Domain;

namespace VietStay.Api.Services;

/// <summary>
/// Quản lý số phòng trống theo từng đêm. Mỗi đêm của mỗi loại phòng là một document RoomNight
/// với bộ đếm <c>booked</c>. Việc giữ phòng dùng cập nhật có điều kiện
/// (<c>booked &lt;= inventory - qty</c>) nên an toàn khi nhiều người đặt cùng lúc mà không cần
/// transaction/replica set; nếu một đêm thất bại thì các đêm đã giữ được hoàn lại.
/// </summary>
public sealed class InventoryService(MongoDb db)
{
    /// <summary>Số phòng đã bán cao nhất trong khoảng ngày, theo từng roomId.</summary>
    public async Task<Dictionary<string, int>> GetPeakBookedAsync(IReadOnlyCollection<string> roomIds, Stay stay, CancellationToken ct)
    {
        if (roomIds.Count == 0) return [];
        var f = Builders<RoomNight>.Filter;
        var nights = await db.RoomNights
            .Find(f.In(r => r.RoomId, roomIds) & f.Gte(r => r.Date, stay.CheckIn) & f.Lt(r => r.Date, stay.CheckOut))
            .Project(r => new { r.RoomId, r.Booked })
            .ToListAsync(ct);
        return nights.GroupBy(n => n.RoomId).ToDictionary(g => g.Key, g => g.Max(n => n.Booked));
    }

    public static int Remaining(Room room, IReadOnlyDictionary<string, int> peakBooked) =>
        Math.Max(0, room.Inventory - peakBooked.GetValueOrDefault(room.Id));

    public static bool Fits(Room room, Stay stay) => room.MaxGuests * stay.Rooms >= stay.Guests;

    /// <summary>Giữ <paramref name="stay"/>.Rooms phòng cho mọi đêm, hoặc ném 409 và không giữ đêm nào.</summary>
    public async Task ReserveAsync(Hotel hotel, Room room, Stay stay, CancellationToken ct)
    {
        var qty = stay.Rooms;
        var reserved = new List<DateOnly>();
        try
        {
            foreach (var night in stay.EachNight())
            {
                var id = RoomNight.Key(room.Id, night);
                await EnsureNightAsync(id, hotel.Id, room.Id, night, ct);

                var result = await db.RoomNights.UpdateOneAsync(
                    r => r.Id == id && r.Booked <= room.Inventory - qty,
                    Builders<RoomNight>.Update.Inc(r => r.Booked, qty),
                    cancellationToken: ct);
                if (result.ModifiedCount == 0)
                {
                    throw ApiException.Conflict($"Rất tiếc, {room.Name} không còn đủ {qty} phòng cho đêm {night:dd/MM/yyyy}.");
                }
                reserved.Add(night);
            }
        }
        catch
        {
            await ReleaseNightsAsync(room.Id, reserved, qty);
            throw;
        }
    }

    public Task ReleaseAsync(string roomId, Stay stay, int qty) => ReleaseNightsAsync(roomId, stay.EachNight().ToList(), qty);

    private async Task ReleaseNightsAsync(string roomId, IReadOnlyCollection<DateOnly> nights, int qty)
    {
        if (nights.Count == 0) return;
        var ids = nights.Select(n => RoomNight.Key(roomId, n)).ToList();
        // Không nhận CancellationToken: việc hoàn phòng phải chạy xong kể cả khi request bị hủy.
        await db.RoomNights.UpdateManyAsync(
            Builders<RoomNight>.Filter.In(r => r.Id, ids),
            Builders<RoomNight>.Update.Inc(r => r.Booked, -qty));
    }

    private async Task EnsureNightAsync(string id, string hotelId, string roomId, DateOnly night, CancellationToken ct)
    {
        try
        {
            await db.RoomNights.UpdateOneAsync(
                r => r.Id == id,
                Builders<RoomNight>.Update
                    .SetOnInsert(r => r.HotelId, hotelId)
                    .SetOnInsert(r => r.RoomId, roomId)
                    .SetOnInsert(r => r.Date, night)
                    .SetOnInsert(r => r.Booked, 0),
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (Exception ex) when (MongoDb.IsDuplicateKey(ex))
        {
            // Một request khác vừa tạo cùng document — không sao.
        }
    }
}
