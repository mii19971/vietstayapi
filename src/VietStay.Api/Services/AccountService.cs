using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using VietStay.Api.Contracts;
using VietStay.Api.Data;
using VietStay.Api.Domain;
using VietStay.Api.Domain.Hbe;

namespace VietStay.Api.Services;

public sealed class AccountService(
    MongoDb db,
    CatalogService catalog,
    IPasswordHasher<User> hasher,
    IOptions<JwtOptions> jwt,
    AppClock clock)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        var v = new Validation();
        v.Check((req.Name?.Trim().Length ?? 0) is >= 2 and <= 100, "name", "Họ tên từ 2 đến 100 ký tự.")
         .Check(req.Email is not null && Text.IsEmail(req.Email), "email", "Email không hợp lệ.")
         .Check(req.Phone is not null && Text.IsPhone(req.Phone), "phone", "Số điện thoại không hợp lệ.")
         .Check((req.Password?.Length ?? 0) is >= 8 and <= 128, "password", "Mật khẩu từ 8 đến 128 ký tự.");
        v.ThrowIfInvalid();

        var user = new User
        {
            Email = req.Email!.Trim().ToLowerInvariant(),
            Name = req.Name!.Trim(),
            Phone = Text.CleanPhone(req.Phone!),
            CreatedAt = clock.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password!);
        try
        {
            await db.Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (Exception ex) when (MongoDb.IsDuplicateKey(ex))
        {
            throw ApiException.Conflict("Email này đã được đăng ký.");
        }
        return IssueToken(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        var result = user is null || string.IsNullOrEmpty(req.Password)
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "Email hoặc mật khẩu không đúng.");
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = hasher.HashPassword(user, req.Password!);
            await db.Users.UpdateOneAsync(u => u.Id == user.Id, Builders<User>.Update.Set(u => u.PasswordHash, user.PasswordHash), cancellationToken: ct);
        }
        return IssueToken(user!);
    }

    public async Task<UserDto> GetProfileAsync(string userId, CancellationToken ct) => (await GetUserAsync(userId, ct)).ToDto();

    public async Task<UserDto> UpdateProfileAsync(string userId, UpdateProfileRequest req, CancellationToken ct)
    {
        var v = new Validation();
        v.Check(req.Name is null || req.Name.Trim().Length is >= 2 and <= 100, "name", "Họ tên từ 2 đến 100 ký tự.")
         .Check(req.Phone is null || Text.IsPhone(req.Phone), "phone", "Số điện thoại không hợp lệ.");
        v.ThrowIfInvalid();

        var update = Builders<User>.Update.Combine();
        if (req.Name is not null) update = update.Set(u => u.Name, req.Name.Trim());
        if (req.Phone is not null) update = update.Set(u => u.Phone, Text.CleanPhone(req.Phone));
        var user = await db.Users.FindOneAndUpdateAsync(
            u => u.Id == userId, update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After }, ct)
            ?? throw ApiException.NotFound("Không tìm thấy tài khoản.");
        return user.ToDto();
    }

    /// <summary>Mới thêm trước. Khách sạn không còn trong hbe (vd. id dữ liệu mẫu cũ) bị bỏ qua.</summary>
    public async Task<IReadOnlyList<HotelSummaryDto>> GetFavoritesAsync(string userId, Lang lang, CancellationToken ct)
    {
        var favorites = (await GetUserAsync(userId, ct)).Favorites;
        return await catalog.GetByIdsAsync(Enumerable.Reverse(favorites).ToList(), lang, ct);
    }

    public async Task AddFavoriteAsync(string userId, string hotelId, CancellationToken ct)
    {
        await catalog.EnsureHotelExistsAsync(hotelId, ct); // 404 nếu khách sạn không tồn tại
        await db.Users.UpdateOneAsync(u => u.Id == userId, Builders<User>.Update.AddToSet(u => u.Favorites, hotelId), cancellationToken: ct);
    }

    public Task RemoveFavoriteAsync(string userId, string hotelId, CancellationToken ct) =>
        db.Users.UpdateOneAsync(u => u.Id == userId, Builders<User>.Update.Pull(u => u.Favorites, hotelId), cancellationToken: ct);

    private async Task<User> GetUserAsync(string userId, CancellationToken ct) =>
        await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct)
        ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Tài khoản không còn tồn tại.");

    private AuthResponse IssueToken(User user)
    {
        var o = jwt.Value;
        var expires = clock.UtcNow.AddMinutes(o.ExpiryMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        return new AuthResponse(token, expires, user.ToDto());
    }
}
