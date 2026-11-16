using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using VietStay.Api.Domain;

namespace VietStay.Api.Data;

/// <summary>Truy cập các collection MongoDB.</summary>
public sealed class MongoDb
{
    public MongoDb(IMongoClient client, IOptions<MongoOptions> options)
    {
        Database = client.GetDatabase(options.Value.Database);
        Cities = Database.GetCollection<City>("cities");
        Hotels = Database.GetCollection<Hotel>("hotels");
        RoomNights = Database.GetCollection<RoomNight>("roomNights");
        Bookings = Database.GetCollection<Booking>("bookings");
        Users = Database.GetCollection<User>("users");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<City> Cities { get; }
    public IMongoCollection<Hotel> Hotels { get; }
    public IMongoCollection<RoomNight> RoomNights { get; }
    public IMongoCollection<Booking> Bookings { get; }
    public IMongoCollection<User> Users { get; }

    public static bool IsDuplicateKey(Exception ex) =>
        ex is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey };
}

public static class MongoSetup
{
    private static int _configured;

    /// <summary>Đăng ký convention/serializer toàn cục (chỉ chạy một lần mỗi tiến trình).</summary>
    public static void ConfigureSerialization()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1) return;

        ConventionRegistry.Register(
            "vietstay",
            new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new EnumRepresentationConvention(BsonType.String),
            },
            _ => true);

        // DateOnly lưu dạng "yyyy-MM-dd" để so sánh khoảng ngày bằng chuỗi và dễ đọc khi tra cứu DB.
        BsonSerializer.RegisterSerializer(new DateOnlyStringSerializer());
    }

    public static async Task CreateIndexesAsync(MongoDb db, CancellationToken ct)
    {
        await db.Hotels.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Hotel>(Builders<Hotel>.IndexKeys.Ascending(h => h.CityId)),
            new CreateIndexModel<Hotel>(Builders<Hotel>.IndexKeys.Descending(h => h.Rating)),
        ], ct);

        await db.RoomNights.Indexes.CreateOneAsync(
            new CreateIndexModel<RoomNight>(Builders<RoomNight>.IndexKeys.Ascending(r => r.RoomId).Ascending(r => r.Date)),
            cancellationToken: ct);

        await db.Bookings.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Booking>(
                Builders<Booking>.IndexKeys.Ascending(b => b.Code),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<Booking>(Builders<Booking>.IndexKeys.Ascending(b => b.UserId).Descending(b => b.CreatedAt)),
        ], ct);

        await db.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);
    }
}

public sealed class DateOnlyStringSerializer : StructSerializerBase<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateOnly value) =>
        context.Writer.WriteString(value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));

    public override DateOnly Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        DateOnly.ParseExact(context.Reader.ReadString(), Format, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Tạo index và nạp dữ liệu mẫu trước khi API nhận request.</summary>
public sealed class MongoInitializer(MongoDb db, IOptions<MongoOptions> options, ILogger<MongoInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await MongoSetup.CreateIndexesAsync(db, ct);
        if (options.Value.Seed)
        {
            await SeedData.UpsertAsync(db, ct);
            logger.LogInformation("Đã nạp dữ liệu mẫu vào database {Database}", options.Value.Database);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
