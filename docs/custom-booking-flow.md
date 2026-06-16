# Luồng Custom Booking

## Mục tiêu

Custom booking là luồng khách thuê nguyên tàu theo nhu cầu riêng.

- Khách không chọn một tàu vật lý cụ thể.
- Khách chỉ mô tả nhu cầu: số tầng, kiểu ghế, số khách, ngày giờ và lịch trình.
- Backend trả giá thuê theo ngày ở mức tham khảo.
- Admin chọn tàu thực tế phù hợp rồi gửi báo giá cuối cùng.
- Sau khi khách đồng ý, yêu cầu chuyển sang `Confirmed`.
- Payment và phân lịch chạy chưa thuộc phạm vi hiện tại.

## Trạng thái

```text
PendingReview --Admin báo giá--> Quoted --Khách đồng ý--> Confirmed
      |                            |
      +--------- hủy -------------+
                                   v
                               Cancelled
```

Chỉ có bốn trạng thái:

| Trạng thái | Ý nghĩa |
|---|---|
| `PendingReview` | Khách vừa gửi yêu cầu, Admin chưa báo giá |
| `Quoted` | Admin đã gán tàu và gửi báo giá |
| `Confirmed` | Khách đã chấp nhận báo giá |
| `Cancelled` | Khách hoặc Admin đã hủy |

Các trạng thái cũ được dọn:

- `QuoteAccepted` được migrate thành `Confirmed`.
- `QuoteRejected` được migrate thành `Cancelled`.

## Luồng khách hàng

### 1. Xem giá tham khảo

```http
GET /api/custom-booking-requests/pricing-options
    ?requestedNumberOfDecks=2
    &requestedSeatSetupType=StandardAndVip
    &passengerCount=20
```

API chỉ trả:

- Số tàu đang phù hợp.
- Khoảng giá thuê theo ngày, nhóm theo currency.
- Không trả `vesselId`, tên tàu hoặc cho khách chọn tàu.

Giá này được lấy từ tàu:

- `Active`.
- Đã setup ghế.
- Đúng số tầng.
- Đúng kiểu ghế.
- Đủ sức chứa.
- Có giá thuê `Day`.

Đây không phải báo giá cuối cùng.

### 2. Gửi yêu cầu

```http
POST /api/custom-booking-requests
```

```json
{
  "useAccountContact": true,
  "contactName": null,
  "contactPhone": null,
  "contactEmail": null,
  "requestedNumberOfDecks": 2,
  "requestedSeatSetupType": "StandardAndVip",
  "departureDate": "20/06/2026",
  "preferredStartTime": "08:30:00",
  "fromStationId": "station-id",
  "toStationId": "station-id",
  "adultCount": 18,
  "childCount": 2,
  "specialRequests": "Trang trí sinh nhật",
  "itineraryStops": [
    {
      "stopOrder": 1,
      "stationId": "station-id",
      "stayDurationMinutes": 90,
      "note": "Tham quan"
    }
  ]
}
```

Khách cần lấy ID từ:

- `fromStationId`, `toStationId`, `itineraryStops[].stationId`: API danh sách station.
- Không cần `vesselId`.
- Không cần service ID.
- Không cần seat type ID. Chỉ gửi enum `FullStandard` hoặc `StandardAndVip`.

Booking luôn phải có email nhận thông tin vé và email phải thuộc `@gmail.com` hoặc `@fpt.edu.vn`.

- `useAccountContact=true`: backend ưu tiên email trong profile.
- Nếu profile chưa có email: khách phải gửi `contactEmail`; email này chỉ lưu trong booking, không cập nhật profile.
- `useAccountContact=false`: bắt buộc gửi `contactName`, `contactPhone` và `contactEmail`.

Backend tự tính:

- Tổng số khách.
- Các chặng.
- Tổng khoảng cách nếu đủ route segment hoặc tọa độ.
- Thời gian di chuyển.
- Thời gian dừng.
- Buffer 10%.
- Ngày và giờ kết thúc dự kiến.

Response có status `PendingReview` và `assignedVessel = null`.

### 3. Sửa yêu cầu

```http
PUT /api/custom-booking-requests/{id}
```

Khách chỉ sửa được khi:

- Là chủ yêu cầu.
- Status là `PendingReview`.
- Admin chưa gán tàu.

API cập nhật toàn bộ phần lịch trình và nhu cầu tàu. Thông tin liên hệ không bị thay đổi.

### 4. Xem báo giá

```http
GET /api/custom-booking-requests/{id}
```

Khi Admin đã báo giá, response có:

- `assignedVessel`.
- `quote.quotedPrice`.
- `quote.depositPercent`.
- `quote.depositAmount`.
- `quote.remainingAmount`.
- `quote.priceNote`.
- `quote.validUntil`.

### 5. Đồng ý báo giá

```http
POST /api/custom-booking-requests/{id}/accept-quote
```

Điều kiện:

- Khách là chủ yêu cầu.
- Status là `Quoted`.
- Báo giá chưa hết hạn.
- Tàu đã gán vẫn Active, còn đúng cấu hình và đủ sức chứa.

Kết quả: status thành `Confirmed`.

### 6. Hủy hoặc từ chối báo giá

```http
POST /api/custom-booking-requests/{id}/cancel
```

```json
{
  "reason": "Không phù hợp ngân sách."
}
```

Khách hủy được khi status là `PendingReview` hoặc `Quoted`.

## Luồng Admin

### 1. Xem danh sách và chi tiết

```http
GET /api/custom-booking-requests
GET /api/custom-booking-requests/{id}
```

Có thể lọc theo:

- `status`.
- `departureDate`.

### 2. Xem tàu phù hợp

```http
GET /api/custom-booking-requests/{id}/vessel-candidates
```

Mỗi candidate đã được backend kiểm tra:

- Tàu Active.
- Đã setup sơ đồ ghế.
- Đúng `requestedNumberOfDecks`.
- Đúng `requestedSeatSetupType`.
- `passengerCapacity >= passengerCount`.
- Có giá thuê theo ngày.

`estimatedBasePrice` được tính:

```text
rentalDays = max(1, ceil(estimatedDurationMinutes / 1440))
estimatedBasePrice = dailyPrice * rentalDays
```

Đây là giá cơ sở để Admin tham khảo, không tự động trở thành báo giá.

### 3. Gán tàu

```http
PUT /api/custom-booking-requests/{id}/assigned-vessel
```

```json
{
  "vesselId": "vessel-id"
}
```

Chỉ gán hoặc đổi tàu khi status là `PendingReview`.

Backend kiểm tra lại toàn bộ điều kiện, không tin dữ liệu candidate cũ.

Nếu khách và Admin cập nhật cùng lúc, request đến sau nhận HTTP `409 Conflict`.
Frontend cần tải lại chi tiết booking rồi cho người dùng thao tác trên trạng thái mới nhất.

### 4. Báo giá

```http
POST /api/custom-booking-requests/{id}/quote
```

```json
{
  "quotedPrice": 15000000,
  "depositPercent": 50,
  "currency": "VND",
  "priceNote": "Đã gồm tiền thuê tàu, chưa gồm dịch vụ phát sinh."
}
```

Backend tính:

```text
depositAmount = round(quotedPrice * depositPercent / 100)
remainingAmount = quotedPrice - depositAmount
validUntil = min(thời điểm báo giá + 24 giờ, thời điểm khởi hành)
```

Admin không nhập `validUntil`. Backend tự đặt thời hạn và trả nó trong `quote.validUntil`.
Admin chỉ báo giá được sau khi gán tàu. Có thể cập nhật báo giá khi status đang là `Quoted`.

### 5. Hủy yêu cầu

Admin/Manager dùng cùng endpoint:

```http
POST /api/custom-booking-requests/{id}/cancel
```

Hệ thống lưu:

- `statusReason`.
- `cancelledAt`.
- `cancelledByUserId`.

### 6. Giao Manager sau khi khách xác nhận

Admin xem Manager đang hoạt động tại bến khởi hành:

```http
GET /api/custom-booking-requests/{id}/manager-candidates
```

Sau đó giao một Manager chịu trách nhiệm:

```http
PUT /api/custom-booking-requests/{id}/assigned-manager
```

```json
{
  "managerUserId": "manager-user-id"
}
```

Điều kiện:

- Booking phải có status `Confirmed`.
- Manager phải có status `Active`.
- Manager phải có `UserStationAssignment` active với `fromStationId`.
- Nếu Admin đổi Manager, kế hoạch Staff và dịch vụ vận hành cũ bị xóa để Manager mới lập lại.

## Luồng Manager

Manager chỉ xem được booking được Admin giao cho mình.

### 1. Xem Staff tại bến khởi hành

```http
GET /api/custom-booking-requests/{id}/staff-candidates
```

API chỉ trả Staff:

- Có status `Active`.
- Có role `Staff`.
- Có `UserStationAssignment` active với `fromStationId`.

### 2. Phân Staff và dịch vụ vận hành

```http
PUT /api/custom-booking-requests/{id}/operation-plan
```

```json
{
  "staffAssignments": [
    {
      "staffUserId": "staff-user-id",
      "dutyNote": "Đón khách và kiểm tra danh sách."
    }
  ],
  "services": [
    {
      "serviceName": "Trang trí sinh nhật",
      "quantity": 1,
      "note": "Thực hiện theo nội dung đã bao gồm trong báo giá."
    }
  ]
}
```

`PUT` thay thế toàn bộ kế hoạch hiện tại.

`services` là dịch vụ vận hành của riêng booking, không dùng `WaterbusServiceId`.
`WaterbusService` hiện là loại hình bán vé/thuê tàu và cấu hình loại ghế, không phải danh mục tiện ích.
Manager không được nhập giá hoặc làm thay đổi báo giá đã được khách xác nhận.
Dịch vụ phát sinh có tính tiền phải chuyển Admin xử lý báo giá lại.

## Phân quyền

| Vai trò | Quyền trong custom booking |
|---|---|
| Customer | Tạo, sửa khi chưa gán tàu, xem booking của mình, chấp nhận hoặc hủy báo giá |
| Admin | Xem tất cả, chọn tàu, báo giá, giao Manager tại bến khởi hành |
| Manager | Chỉ xem booking được giao, chọn Staff và lập kế hoạch dịch vụ vận hành |
| Staff | Chỉ xem booking được Manager phân công |

## Validation chính

| Field/điều kiện | Validation |
|---|---|
| `requestedNumberOfDecks` | Từ 1 đến 10 |
| `requestedSeatSetupType` | `FullStandard` hoặc `StandardAndVip` |
| Ngày giờ đi | Phải lớn hơn thời điểm hiện tại theo UTC+7 |
| Người lớn | Ít nhất 1 |
| Trẻ em | Không âm |
| Tổng khách | Không vượt quá 500 |
| Điểm ghé | Tối đa 20 |
| `stopOrder` | Bắt đầu từ 1, liên tục, không trùng |
| Thời gian dừng | 0 đến 1440 phút |
| Lịch trình | Hai điểm liên tiếp không được trùng |
| `specialRequests` | Tối đa 1000 ký tự |
| Email nhận vé | Bắt buộc; lấy từ profile hoặc `contactEmail`, tối đa 255 ký tự, thuộc `@gmail.com` hoặc `@fpt.edu.vn` |
| Tàu Admin gán | Active, setup xong, đúng cấu hình, đủ sức chứa, có giá |
| Báo giá | Giá > 0, cọc lớn hơn 0 và không quá 100%; giờ khởi hành chưa qua |
| Hủy | Chỉ `PendingReview` hoặc `Quoted`, bắt buộc lý do |
| Manager được giao | Active, role Manager, phụ trách bến khởi hành |
| Staff được phân | Active, role Staff, phụ trách bến khởi hành |
| Kế hoạch vận hành | Tối đa 50 Staff và 30 dịch vụ; Staff/tên dịch vụ không trùng |

## API cũ đã loại khỏi luồng

Đã xóa:

```http
GET /api/fares/vessel-rental-prices
```

Lý do: API này trả danh sách tàu và `vesselId` cho khách, trái với nghiệp vụ mới.

Vẫn giữ API Admin cấu hình giá nguồn:

```http
PUT /api/fares/vessel-rental-prices/{vesselId}
```

## Phạm vi chưa xử lý

- Chưa kiểm tra trùng lịch tàu vì `Trip` hiện chưa có `VesselId`.
- Chưa tạo payment.
- Chưa tự tạo trip/lịch chạy sau `Confirmed`.
- Chưa có catalog dịch vụ tiện ích và đơn giá riêng; dịch vụ vận hành hiện lưu theo từng booking.
