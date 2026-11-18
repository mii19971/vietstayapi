using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using VietStay.Api.Domain;
using VietStay.Api.Domain.Hbe;

namespace VietStay.Api.Data;

/// <summary>Database VietStay (đọc/ghi): tài khoản và đơn đặt phòng.</summary>
public sealed class MongoDb
{
    public MongoDb(IMongoClient client, IOptions<MongoOptions> options)
    {
        Database = client.GetDatabase(options.Value.Database);
        Bookings = Database.GetCollection<Booking>("bookings");
        Users = Database.GetCollection<User>("users");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<Booking> Bookings { get; }
    public IMongoCollection<User> Users { get; }

    public static bool IsDuplicateKey(Exception ex) =>
        ex is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey };
}

/// <summary>Database nội dung hbe (chỉ đọc).</summary>
public sealed class HbeDb
{
    public HbeDb(IMongoClient defaultClient, IOptions<HbeOptions> options)
    {
        var o = options.Value;
        var client = string.IsNullOrWhiteSpace(o.ConnectionString) ? defaultClient : new MongoClient(o.ConnectionString);
        Database = client.GetDatabase(o.Database);
        Countries = Database.GetCollection<HbeCountry>("country");
        References = Database.GetCollection<HbeReference>("references");
        Regions = Database.GetCollection<HbeRegion>("epa_regions");
        Places = Database.GetCollection<HbePlace>("places");
        ViProperties = Database.GetCollection<HbeProperty>("vi_eap_properties");
        EnProperties = Database.GetCollection<HbeProperty>("en_eap_properties");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<HbeCountry> Countries { get; }
    public IMongoCollection<HbeReference> References { get; }
    public IMongoCollection<HbeRegion> Regions { get; }
    public IMongoCollection<HbePlace> Places { get; }
    public IMongoCollection<HbeProperty> ViProperties { get; }
    public IMongoCollection<HbeProperty> EnProperties { get; }

    public IMongoCollection<HbeProperty> Properties(Lang lang) => lang == Lang.En ? EnProperties : ViProperties;
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

    /// <summary>Chỉ tạo index trên database VietStay. Database hbe là chỉ đọc — không đụng tới.</summary>
    public static async Task CreateIndexesAsync(MongoDb db, CancellationToken ct)
    {
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

/// <summary>Tạo index cho database VietStay trước khi API nhận request.</summary>
public sealed class MongoInitializer(MongoDb db, IOptions<MongoOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await MongoSetup.CreateIndexesAsync(db, ct);
        }
        catch (Exception ex) when (ex is MongoAuthenticationException or MongoCommandException { Code: 13 or 18 })
        {
            // 13 = Unauthorized (thiếu tài khoản hoặc không đủ quyền), 18 = AuthenticationFailed (sai mật khẩu).
            throw new InvalidOperationException(
                $"MongoDB từ chối truy cập database '{options.Value.Database}': {ex.Message} " +
                "Hãy đặt tài khoản vào Mongo:ConnectionString, dạng mongodb://<user>:<password>@<host>:27017/?authSource=admin, " +
                "qua user-secrets hoặc biến môi trường Mongo__ConnectionString (đừng ghi mật khẩu vào appsettings.json). " +
                "User cần có quyền readWrite trên database này.",
                ex);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
