# VietStay API

Backend cho app đặt phòng khách sạn VietStay — **.NET 10 (ASP.NET Core Minimal API) + MongoDB**.

Dùng hai database trên cùng server MongoDB:

- **`hbe`** (chỉ đọc) — nội dung khách sạn: `country`, `vi_eap_properties`, `en_eap_properties`, `epa_regions`, `places`, `references`.
  API không ghi và không tạo index trên database này.
- **`vietstay`** (đọc/ghi) — `users` (tài khoản, yêu thích) và `bookings` (yêu cầu đặt phòng).

## Chạy local

Yêu cầu: .NET 10 SDK và quyền truy cập MongoDB có database `hbe` (đọc) và `vietstay` (đọc/ghi).
Server hiện tại chạy MongoDB 4.4 — các aggregation chỉ dùng toán tử có từ 4.4.

```bash
dotnet run --project src/VietStay.Api
```

- API: http://localhost:5045 — tài liệu tương tác (Scalar): http://localhost:5045/scalar
- OpenAPI JSON: http://localhost:5045/openapi/v1.json
- Health check: http://localhost:5045/health
- Khi khởi động, API nạp sẵn (chạy nền) danh sách khách sạn của các điểm đến phổ biến vào bộ nhớ (`Hbe:WarmUp`).

Đặt connection string qua user-secrets thay vì ghi vào `appsettings.json`:

```bash
dotnet user-secrets --project src/VietStay.Api set "Mongo:ConnectionString" "mongodb://<user>:<password>@<host>:27017/?authSource=admin"
```

## Test

```bash
dotnet test
```

Test tích hợp chạy API thật trên một MongoDB tạm do [EphemeralMongo](https://github.com/asimmon/ephemeral-mongo)
tự tải và khởi chạy (lần đầu tải ~700 MB vào `%LOCALAPPDATA%\ephemeral-mongo`, các lần sau dùng lại).
Test không chạm vào server thật: một bản thu nhỏ của `hbe` (cùng tên collection/trường/kiểu dữ liệu, text index trên `places`)
được nạp vào MongoDB tạm — xem `tests/VietStay.Api.Tests/HbeTestData.cs`.
Test dùng MongoDB 7.0 vì bản 8.x mới nhất không khởi động được trên Windows 10 (mongod thoát với mã `0xC0000139`).

## Cấu hình (`appsettings.json`)

| Khóa | Ý nghĩa |
|---|---|
| `Mongo:ConnectionString`, `Mongo:Database` | Database VietStay (users, bookings) |
| `Hbe:ConnectionString` | Để trống = dùng chung `Mongo:ConnectionString` |
| `Hbe:Database` | Database nội dung khách sạn (mặc định `hbe`) |
| `Hbe:Destinations` | Điểm đến phổ biến: `[{ RegionId, Image }]` (`region_id` trong `epa_regions`) |
| `Hbe:FeaturedFromDestinations`, `Hbe:FeaturedMinReviews` | "Nổi bật" lấy từ N điểm đến đầu, tối thiểu bao nhiêu lượt đánh giá |
| `Hbe:CacheMinutes` | Thời gian giữ danh sách khách sạn của một vùng trong bộ nhớ (mặc định 30) |
| `Hbe:SuggestTimeoutMs` | Giới hạn thời gian truy vấn full-text trên `places` (mặc định 4000) |
| `Hbe:WarmUp` | Nạp sẵn các điểm đến khi khởi động |
| `Jwt:SigningKey` | Khóa ký JWT, **≥ 32 ký tự**. Bắt buộc ở production — đặt qua biến môi trường `Jwt__SigningKey` hoặc user-secrets, không commit |
| `Jwt:ExpiryMinutes` | Thời hạn token (mặc định 7 ngày) |
| `Booking:MaxNights`, `MaxGuests`, `MaxRooms` | Giới hạn khi gửi yêu cầu đặt phòng |
| `Booking:TimeZone` | Múi giờ xác định "hôm nay" (mặc định `Asia/Ho_Chi_Minh`) |
| `Cors:Origins` | Origin được phép gọi API từ trình duyệt (Development: mọi cổng localhost) |

## Dữ liệu hbe được dùng thế nào

| Collection | Vai trò | Liên kết |
|---|---|---|
| `places` | Ô tìm kiếm (text index `en_name` + `vi_non_mark_name`) | `type=Region` → `epa_region_id`; `type=Property` → `epa_external_property_id` |
| `epa_regions` | Vùng: quốc gia → tỉnh → thành phố → khu phố, sân bay, địa danh | `external_property_ids` = khách sạn trong vùng (`external_property_expanded_ids` = kể cả lân cận) |
| `vi_eap_properties` / `en_eap_properties` | Nội dung khách sạn theo ngôn ngữ (cùng cấu trúc) | khóa `external_property_id` (có index) |
| `references` | Bảng tra mã | `epa_id` = khóa trong `amenities`, `attributes.pets`... của khách sạn |
| `country` | Danh sách quốc gia | `country_code` |

Ghi chú dữ liệu:

- Điểm khách lưu dạng chuỗi thang 5 (`"4.6"`) → API trả **thang 10** (`9.2`). Hạng sao dạng chuỗi (`"4.5"`).
- Mô tả/chính sách là HTML → API trả **văn bản thuần**. Một số tên lưu Unicode dạng tổ hợp → API chuẩn hóa NFC.
- **hbe không có giá và số phòng trống**, vì vậy đặt phòng là *yêu cầu* (`pending`) và các trường giá để `null`.

## Endpoints

Danh mục nhận `?lang=vi|en` (mặc định `vi`). Id khách sạn = `external_property_id`, id vùng = `region_id`.
Ngày dạng `yyyy-MM-dd`. Lỗi trả về theo chuẩn `application/problem+json`; lỗi dữ liệu (400) có `errors: { field: [..] }`.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/countries` | | Quốc gia |
| GET | `/api/destinations` | | Điểm đến phổ biến (cấu hình) + số khách sạn |
| GET | `/api/places/suggest?q=&limit=10` | | Gợi ý khi gõ: vùng và khách sạn khớp **mọi từ** (không dấu được, theo từ hoàn chỉnh); ưu tiên Việt Nam, vùng trước khách sạn |
| GET | `/api/regions/{id}` | | Thông tin vùng |
| GET | `/api/hotels/search` | | Tìm khách sạn (xem bên dưới) |
| GET | `/api/hotels/featured?limit=6` | | Khách sạn đánh giá cao, xen kẽ giữa các điểm đến |
| GET | `/api/hotels/{id}` | | Chi tiết: ảnh, tiện nghi, mô tả, phòng, chính sách, điểm thành phần |
| GET | `/api/references?type=&ids=` | | Bảng tra cứu (`amenities_property`, `amenities_rooms`, `attributes_pets`, `categories`, `room_views`, `bed_groups`...) |
| POST | `/api/auth/register` | | Đăng ký `{name,email,phone,password}` → `{token,expiresAt,user}` |
| POST | `/api/auth/login` | | Đăng nhập `{email,password}` → `{token,expiresAt,user}` |
| GET / PUT | `/api/me` | ✓ | Xem / sửa `{name,phone}` |
| GET | `/api/me/favorites` | ✓ | Khách sạn yêu thích (mới thêm trước) |
| PUT / DELETE | `/api/me/favorites/{hotelId}` | ✓ | Thêm / bỏ yêu thích |
| POST | `/api/bookings` | ✓ | Gửi yêu cầu đặt phòng |
| GET | `/api/bookings` | ✓ | Đơn của tôi (mới nhất trước) |
| GET | `/api/bookings/{id}` | ✓ | Chi tiết đơn |
| POST | `/api/bookings/{id}/cancel` | ✓ | Hủy đơn (trước ngày nhận phòng) |

Auth: header `Authorization: Bearer <token>`. Đăng ký/đăng nhập giới hạn 10 request/phút/IP.

**Tìm kiếm** `/api/hotels/search`: cần `regionId` hoặc `q` (từ khóa → vùng khớp nhất; nếu chỉ khớp tên khách sạn thì
trả các khách sạn đó với `region = null`). Lọc `minStars` (0–5), `minRating` (thang 10),
`amenities=wifi,pool` (`wifi, pool, spa, gym, restaurant, parking, breakfast, airport, beach, pet` — ánh xạ sang `epa_id`
trong `Services/HbeMapping.cs`), `sort=recommended|rating|stars|name`, `page`, `pageSize` (≤ 50).

**Đặt phòng** `POST /api/bookings`:

```json
{
  "hotelId": "910861",
  "roomId": "201234567",
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

`roomId` lấy từ `rooms[].id` của chi tiết khách sạn; sức chứa kiểm tra theo `occupancy.max_allowed.total` của phòng.
Trả về `201` + đơn (`code` dạng `VSXXXXXX`, `status: pending|confirmed|cancelled`, các trường giá `null`, `canCancel`).

## Thiết kế

```
src/VietStay.Api/
  Program.cs                 # DI, JWT, CORS, rate limit, OpenAPI, health check
  Endpoints.cs               # Khai báo route
  Contracts.cs               # DTO request/response
  Domain/Hbe.cs              # Ánh xạ chỉ đọc các collection hbe
  Domain/Models.cs           # Document VietStay: Booking, User
  Data/Mongo.cs              # Kết nối 2 database, convention, index (chỉ database vietstay)
  Services/CatalogService.cs # Truy vấn hbe, cache theo vùng, tìm kiếm, gợi ý
  Services/HbeMapping.cs     # hbe → DTO, mã tiện nghi chính
  Services/                  # Đặt phòng, tài khoản
tests/VietStay.Api.Tests/    # Test tích hợp trên MongoDB tạm + dữ liệu hbe thu nhỏ
```

- **Hiệu năng**: document khách sạn nặng 30–60 KB và lần đọc đầu một vùng có thể mất vài giây (đĩa server). API chỉ lấy
  bản tóm tắt bằng aggregation (`$project`), giữ trong bộ nhớ theo vùng/ngôn ngữ (`Hbe:CacheMinutes`), rồi lọc/sắp xếp/
  phân trang trên bộ nhớ; các request đồng thời dùng chung một lần nạp.
- **Gợi ý**: dùng text index có sẵn của `places`, mỗi từ là một phrase (bắt buộc khớp). Từ gõ dở (`"da na"`) chưa khớp —
  muốn gợi ý theo tiền tố cần thêm index mới trên hbe.
- **Mật khẩu** băm bằng `PasswordHasher` của ASP.NET Core Identity (PBKDF2).
