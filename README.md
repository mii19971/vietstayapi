# VietStay API

Backend cho app đặt phòng khách sạn VietStay — **.NET 10 (ASP.NET Core Minimal API) + MongoDB**.

## Chạy local

Yêu cầu: .NET 10 SDK và một MongoDB đang chạy (mặc định `mongodb://localhost:27017`).

```bash
dotnet run --project src/VietStay.Api
```

- API: http://localhost:5045 — tài liệu tương tác (Scalar): http://localhost:5045/scalar
- OpenAPI JSON: http://localhost:5045/openapi/v1.json
- Health check: http://localhost:5045/health
- Ở môi trường Development, dữ liệu mẫu (8 thành phố, 11 khách sạn — trùng ID với dữ liệu mock của app) được nạp tự động.

Chưa cài MongoDB? Có thể dùng Docker: `docker run -d -p 27017:27017 mongo:8`, hoặc MongoDB Atlas (gói miễn phí)
rồi đặt connection string:

```bash
dotnet user-secrets --project src/VietStay.Api set "Mongo:ConnectionString" "mongodb+srv://..."
```

## Test

```bash
dotnet test
```

Test tích hợp chạy API thật trên một MongoDB tạm do [EphemeralMongo](https://github.com/asimmon/ephemeral-mongo)
tự tải và khởi chạy (lần đầu tải ~700 MB vào `%LOCALAPPDATA%\ephemeral-mongo`, các lần sau dùng lại).
Không cần cài MongoDB để chạy test. Windows có thể hỏi quyền tường lửa cho `mongod.exe` ở lần đầu.
Test dùng MongoDB 7.0 vì bản 8.x mới nhất không khởi động được trên Windows 10 (mongod thoát với mã `0xC0000139`);
API hoạt động với cả MongoDB 7 và 8.

## Cấu hình (`appsettings.json`)

| Khóa | Ý nghĩa |
|---|---|
| `Mongo:ConnectionString`, `Mongo:Database` | Kết nối MongoDB |
| `Mongo:Seed` | Nạp/cập nhật dữ liệu mẫu khi khởi động (bật sẵn ở Development) |
| `Jwt:SigningKey` | Khóa ký JWT, **≥ 32 ký tự**. Bắt buộc ở production — đặt qua biến môi trường `Jwt__SigningKey` hoặc user-secrets, không commit |
| `Jwt:ExpiryMinutes` | Thời hạn token (mặc định 7 ngày) |
| `Booking:TaxRate`, `MaxNights`, `MaxGuests`, `MaxRooms` | Quy tắc đặt phòng |
| `Booking:TimeZone` | Múi giờ xác định "hôm nay" (mặc định `Asia/Ho_Chi_Minh`) |
| `Cors:Origins` | Origin được phép gọi API từ trình duyệt (Development: Expo web `http://localhost:8081`) |

## Endpoints

Ngày dạng `yyyy-MM-dd`. Lỗi trả về theo chuẩn `application/problem+json`; lỗi dữ liệu (400) có `errors: { field: [..] }`.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/cities` | | Điểm đến |
| GET | `/api/hotels/featured?limit=6` | | Khách sạn đánh giá cao nhất |
| GET | `/api/hotels/search` | | Tìm khách sạn còn phòng (xem tham số bên dưới) |
| GET | `/api/hotels/suggest?q=&checkIn&checkOut&guests&rooms&limit=20&radiusKm=20` | | Gợi ý khi gõ: tối đa `limit` khách sạn gần địa điểm nhất, còn phòng, sắp theo giá thấp → cao |
| GET | `/api/hotels/{id}` | | Chi tiết khách sạn |
| GET | `/api/hotels/{id}/rooms?checkIn&checkOut&guests&rooms` | | Phòng trống + giá theo khoảng ngày |
| POST | `/api/auth/register` | | Đăng ký `{name,email,phone,password}` → `{token,expiresAt,user}` |
| POST | `/api/auth/login` | | Đăng nhập `{email,password}` → `{token,expiresAt,user}` |
| GET / PUT | `/api/me` | ✓ | Xem / sửa `{name,phone}` |
| GET | `/api/me/favorites` | ✓ | Khách sạn yêu thích |
| PUT / DELETE | `/api/me/favorites/{hotelId}` | ✓ | Thêm / bỏ yêu thích |
| POST | `/api/bookings` | ✓ | Đặt phòng |
| GET | `/api/bookings` | ✓ | Đơn của tôi (mới nhất trước) |
| GET | `/api/bookings/{id}` | ✓ | Chi tiết đơn |
| POST | `/api/bookings/{id}/cancel` | ✓ | Hủy đơn (trước ngày nhận phòng) |

Auth: header `Authorization: Bearer <token>`. Đăng ký/đăng nhập giới hạn 10 request/phút/IP.

**Tìm kiếm** `/api/hotels/search`: `q` (không dấu cũng được: `da nang`), `checkIn`, `checkOut` (mặc định: ngày mai, 1 đêm),
`guests` (mặc định 2), `rooms` (mặc định 1), `maxPrice`, `minStars`, `minRating`, `amenities=wifi,pool`,
`sort=recommended|price-asc|price-desc|rating`, `page`, `pageSize` (≤ 50).

**Đặt phòng** `POST /api/bookings`:

```json
{
  "hotelId": "dn-my-khe-ocean",
  "roomId": "dn-my-khe-ocean-dlx",
  "checkIn": "2026-12-20",
  "checkOut": "2026-12-22",
  "guests": 2,
  "rooms": 1,
  "guestName": "Nguyễn Văn A",
  "guestEmail": "a@example.com",
  "guestPhone": "0912345678",
  "note": "Phòng tầng cao",
  "paymentMethod": "pay_at_hotel"
}
```

`paymentMethod`: `pay_at_hotel` | `bank_transfer` | `card`. Trả về `201` + đơn (`code` dạng `VSXXXXXX`, `subtotal`, `taxes`, `total`,
`status: confirmed|cancelled`, `canCancel`). Hết phòng → `409`.

## Thiết kế

```
src/VietStay.Api/
  Program.cs            # DI, JWT, CORS, rate limit, OpenAPI, health check
  Endpoints.cs          # Khai báo route
  Contracts.cs          # DTO request/response + mapping
  Domain/Models.cs      # Document MongoDB: City, Hotel (Room/Review nhúng), Booking, User, RoomNight
  Data/                 # Kết nối, convention, index, dữ liệu mẫu
  Services/             # Nghiệp vụ: tìm kiếm, phòng trống, đặt/hủy, tài khoản
tests/VietStay.Api.Tests/  # Test tích hợp trên MongoDB tạm
```

- **Chống đặt trùng (overbooking)**: mỗi đêm của mỗi loại phòng là một document `roomNights` có bộ đếm `booked`.
  Giữ phòng bằng cập nhật có điều kiện `booked <= inventory - qty` cho từng đêm; nếu một đêm thất bại thì hoàn lại các đêm
  đã giữ. Cách này an toàn khi nhiều người đặt cùng lúc mà không cần transaction (chạy được trên MongoDB standalone).
  Có test đặt đồng thời 8 đơn cho 3 phòng → đúng 3 đơn thành công.
- **Giá**: lưu VND dạng số nguyên (`long`); giá và thông tin khách sạn được chụp lại vào đơn tại thời điểm đặt.
- **Tìm không dấu**: trường `searchText` lưu tên + thành phố + địa chỉ đã bỏ dấu.
- **Gợi ý gần nhất** (`/api/hotels/suggest`): từ khóa được nhận diện thành địa điểm — thành phố (khớp tên, không dấu,
  tên gọi khác như "sai gon", "hcm") hoặc tên khách sạn. Khách sạn lưu vị trí dạng GeoJSON (`location`, index `2dsphere`),
  truy vấn `$nearSphere` lấy các khách sạn gần nhất trong `radiusKm`, bỏ khách sạn hết phòng, giữ tối đa `limit`,
  rồi sắp theo giá thấp → cao. Tọa độ trong dữ liệu mẫu là gần đúng.
- **Mật khẩu** băm bằng `PasswordHasher` của ASP.NET Core Identity (PBKDF2).
