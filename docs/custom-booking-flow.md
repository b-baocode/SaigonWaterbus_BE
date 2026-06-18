# Luồng Custom Booking

## Mục tiêu

Custom booking là luồng khách thuê nguyên tàu theo nhu cầu riêng.

- Khách không chọn một tàu vật lý cụ thể.
- Khách chỉ mô tả nhu cầu: số tầng, kiểu ghế, số khách, ngày giờ và lịch trình.
- Backend trả giá thuê tham khảo theo `Hour` hoặc `Day` khách chọn.
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

## API runbook để chạy thủ công

Phần này dùng để test bằng Swagger, Postman hoặc `curl`.

Biến mẫu:

```bash
BASE_URL="http://localhost:5000"
TOKEN_CUSTOMER="customer-jwt"
TOKEN_ADMIN="admin-jwt"
TOKEN_MANAGER="manager-jwt"
TOKEN_STAFF="staff-jwt"

CUSTOM_BOOKING_ID="00000000-0000-0000-0000-000000000000"
FROM_STATION_ID="00000000-0000-0000-0000-000000000001"
TO_STATION_ID="00000000-0000-0000-0000-000000000002"
STOP_STATION_ID="00000000-0000-0000-0000-000000000003"
VESSEL_ID="00000000-0000-0000-0000-000000000004"
MANAGER_USER_ID="00000000-0000-0000-0000-000000000005"
STAFF_USER_ID="00000000-0000-0000-0000-000000000006"
```

Lưu ý format:

- Enum gửi dạng string: `FullStandard`, `StandardAndVip`, `Hour`, `Day`, `PendingReview`, `Quoted`, `Confirmed`, `Cancelled`.
- JSON body đang nhận `DateOnly` theo `dd/MM/yyyy` hoặc `dd-MM-yyyy`. Ví dụ: `20/06/2026`.
- Query `departureDate` nhận `dd/MM/yyyy`, `dd-MM-yyyy` hoặc `yyyy-MM-dd`.
- Tất cả API đều cần header `Authorization: Bearer ...`.

### 1. Xem service thuê tàu

Endpoint này chủ yếu để FE/Admin xem cấu hình; khách tạo custom booking không cần gửi `serviceId`.

```http
GET /api/custom-booking-requests/rental-services
```

```bash
curl "$BASE_URL/api/custom-booking-requests/rental-services" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu:

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000010",
    "code": "WT",
    "name": "Water Taxi",
    "bookingMode": "VesselRental"
  }
]
```

### 2. Khách xem giá tham khảo

```http
GET /api/custom-booking-requests/pricing-options
    ?requestedNumberOfDecks=2
    &requestedSeatSetupType=StandardAndVip
    &rentalUnit=Hour
    &passengerCount=8
```

```bash
curl "$BASE_URL/api/custom-booking-requests/pricing-options?requestedNumberOfDecks=2&requestedSeatSetupType=StandardAndVip&rentalUnit=Hour&passengerCount=8" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu:

```json
{
  "requestedNumberOfDecks": 2,
  "requestedSeatSetupType": "StandardAndVip",
  "rentalUnit": "Hour",
  "passengerCount": 8,
  "matchingVesselCount": 2,
  "priceRanges": [
    {
      "currency": "VND",
      "rentalUnit": "Hour",
      "minimumPrice": 2000000,
      "maximumPrice": 2500000
    }
  ],
  "note": "Đây là giá thuê tàu tham khảo theo đơn vị thuê khách chọn, giá cuối sẽ được hệ thống tính sau khi Admin gán tàu."
}
```

### 3. Khách tạo custom booking

```http
POST /api/custom-booking-requests
```

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -H "Content-Type: application/json" \
  -d '{
    "useAccountContact": true,
    "contactName": null,
    "contactPhone": null,
    "contactEmail": "customer@gmail.com",
    "requestedNumberOfDecks": 2,
    "requestedSeatSetupType": "StandardAndVip",
    "rentalUnit": "Hour",
    "departureDate": "20/06/2026",
    "preferredStartTime": "08:30:00",
    "fromStationId": "'"$FROM_STATION_ID"'",
    "toStationId": "'"$TO_STATION_ID"'",
    "adultCount": 6,
    "childCount": 2,
    "specialRequests": "Cần hỗ trợ trang trí sinh nhật.",
    "itineraryStops": [
      {
        "stopOrder": 1,
        "stationId": "'"$STOP_STATION_ID"'",
        "stayDurationMinutes": 90,
        "note": "Tham quan"
      }
    ]
  }'
```

Response cần lấy:

```json
{
  "id": "CUSTOM_BOOKING_ID",
  "status": "PendingReview",
  "passengerCount": 8,
  "adultCount": 6,
  "childCount": 2,
  "passengerManifestStatus": "NotStarted",
  "assignedVessel": null,
  "quote": null
}
```

Sau bước này lưu `id` vào `CUSTOM_BOOKING_ID`.

### 4. Xem danh sách custom booking

```http
GET /api/custom-booking-requests
GET /api/custom-booking-requests?status=PendingReview
GET /api/custom-booking-requests?departureDate=20/06/2026
```

```bash
curl "$BASE_URL/api/custom-booking-requests?status=PendingReview" \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

Quyền xem:

- Customer chỉ thấy booking của mình.
- Admin thấy tất cả.
- Manager chỉ thấy booking được giao.
- Staff chỉ thấy booking được phân công.

### 5. Xem chi tiết custom booking

```http
GET /api/custom-booking-requests/{id}
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu rút gọn:

```json
{
  "id": "CUSTOM_BOOKING_ID",
  "requestedNumberOfDecks": 2,
  "requestedSeatSetupType": "StandardAndVip",
  "rentalUnit": "Hour",
  "departureDate": "20/06/2026",
  "preferredStartTime": "08:30:00",
  "passengerCount": 8,
  "adultCount": 6,
  "childCount": 2,
  "passengerManifestStatus": "NotStarted",
  "status": "PendingReview",
  "routeEstimate": {
    "estimatedDurationText": "2 giờ 45 phút",
    "estimatedEndDate": "20/06/2026",
    "estimatedEndTime": "11:15:00"
  }
}
```

### 6. Khách sửa yêu cầu trước khi Admin gán tàu

```http
PUT /api/custom-booking-requests/{id}
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -H "Content-Type: application/json" \
  -d '{
    "requestedNumberOfDecks": 2,
    "requestedSeatSetupType": "StandardAndVip",
    "rentalUnit": "Hour",
    "departureDate": "20/06/2026",
    "preferredStartTime": "09:00:00",
    "fromStationId": "'"$FROM_STATION_ID"'",
    "toStationId": "'"$TO_STATION_ID"'",
    "adultCount": 6,
    "childCount": 2,
    "specialRequests": "Đổi giờ khởi hành sang 09:00.",
    "itineraryStops": []
  }'
```

Điều kiện:

- Customer là chủ booking.
- `status = PendingReview`.
- Admin chưa gán tàu.

### 7. Xem tàu phù hợp còn trống

```http
GET /api/custom-booking-requests/{id}/vessel-candidates
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/vessel-candidates" \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

Response mẫu:

```json
[
  {
    "vesselId": "VESSEL_ID",
    "code": "WB01",
    "name": "Waterbus 01",
    "seatCount": 30,
    "numberOfDecks": 2,
    "seatSetupType": "StandardAndVip",
    "rentalPrices": [
      {
        "rentalUnit": "Hour",
        "unitPrice": 2000000,
        "estimatedBasePrice": 5500000,
        "currency": "VND",
        "priceNote": "Giá thuê theo giờ"
      }
    ]
  }
]
```

Customer chủ booking cũng có thể xem candidates khi booking còn `PendingReview`.

### 8. Khách chọn tàu mong muốn

Đây chỉ là preference của khách, chưa phải tàu chính thức.

```http
PUT /api/custom-booking-requests/{id}/preferred-vessel
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/preferred-vessel" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -H "Content-Type: application/json" \
  -d '{
    "vesselId": "'"$VESSEL_ID"'"
  }'
```

### 9. Admin gán tàu chính thức

```http
PUT /api/custom-booking-requests/{id}/assigned-vessel
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/assigned-vessel" \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  -H "Content-Type: application/json" \
  -d '{
    "vesselId": "'"$VESSEL_ID"'"
  }'
```

Điều kiện:

- Chỉ Admin.
- Booking còn `PendingReview`.
- Backend kiểm tra lại tàu Active, đã setup ghế, đúng cấu hình, đủ sức chứa, có giá theo `rentalUnit`, và không trùng lịch giữ tàu.

### 10. Admin báo giá

```http
POST /api/custom-booking-requests/{id}/quote
```

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/quote" \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  -H "Content-Type: application/json" \
  -d '{
    "depositPercent": 50,
    "serviceFeeAmount": 400000,
    "priceNote": "Giá đã gồm tàu, nhân sự vận hành và trang trí cơ bản."
  }'
```

Response mẫu:

```json
{
  "id": "CUSTOM_BOOKING_ID",
  "status": "Quoted",
  "assignedVessel": {
    "id": "VESSEL_ID",
    "code": "WB01",
    "name": "Waterbus 01"
  },
  "quote": {
    "quotedPrice": 5500000,
    "serviceFeeAmount": 400000,
    "depositPercent": 50,
    "depositAmount": 2750000,
    "remainingAmount": 2750000,
    "currency": "VND",
    "priceNote": "Giá đã gồm tàu, nhân sự vận hành và trang trí cơ bản.",
    "validUntil": "2026-06-19T02:00:00+00:00"
  }
}
```

Admin không gửi `quotedPrice`; backend tự tính từ giá tàu, thời lượng thuê và `serviceFeeAmount`. Nếu không tính phụ phí thì bỏ `serviceFeeAmount` hoặc gửi `0`.

### 11. Khách chấp nhận báo giá

```http
POST /api/custom-booking-requests/{id}/accept-quote
```

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/accept-quote" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu:

```json
{
  "id": "CUSTOM_BOOKING_ID",
  "status": "Confirmed",
  "passengerManifestStatus": "NotStarted",
  "ticket": null
}
```

Sau bước này:

- Booking đã được chốt ở mức báo giá, nhưng backend chưa tạo/gửi QR tại bước này.
- Khách phải hoàn tất passenger manifest trước khi check-in.
- QR được phát khi manifest chuyển sang `Completed`; nếu cần khóa đúng theo đặt cọc thật sự thì cần thêm trạng thái payment/deposit.

### 12. Xem vé QR

```http
GET /api/custom-booking-requests/{id}/ticket
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/ticket" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu cho Customer/Admin/Manager được giao:

```json
{
  "id": "00000000-0000-0000-0000-000000000020",
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "ticketCode": "CBT-20260618-ABC12345",
  "status": "Active",
  "qrPayload": "swb:custom-booking:raw-token",
  "qrIssuedAt": "2026-06-18T03:00:00+00:00",
  "qrExpiresAt": "2026-06-20T04:15:00+00:00",
  "qrUsedAt": null
}
```

Staff được phân công gọi API này chỉ nhận metadata, `qrPayload = null`.

### 13. Xem passenger manifest hiện tại

```http
GET /api/custom-booking-requests/{id}/passengers
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/passengers" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER"
```

Response mẫu:

```json
{
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "status": "NotStarted",
  "requiredPassengerCount": 8,
  "requiredAdultCount": 6,
  "requiredChildCount": 2,
  "passengerCount": 0,
  "adultCount": 0,
  "childCount": 0,
  "completedAt": null,
  "passengers": []
}
```

### 14. Preview upload file passenger manifest

Endpoint này chỉ parse file, tính adult/child, trả lỗi/cảnh báo tổng và không lưu DB.

File CSV mẫu:

```csv
FullName,DateOfBirth
Nguyen Van A,20/06/1995
Nguyen Van C,21/06/2018
```

Chạy thử:

```bash
cat > passengers.csv <<'CSV'
FullName,DateOfBirth
Nguyen Van A,20/06/1995
Nguyen Van C,21/06/2018
CSV

curl -X POST "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/passengers/import/preview" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -F "file=@passengers.csv"
```

Response mẫu:

```json
{
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "canConfirm": true,
  "requiredPassengerCount": 2,
  "requiredAdultCount": 1,
  "requiredChildCount": 1,
  "passengerCount": 2,
  "adultCount": 1,
  "childCount": 1,
  "errors": [],
  "warnings": [],
  "rows": [
    {
      "rowNumber": 2,
      "fullName": "Nguyen Van A",
      "dateOfBirth": "20/06/1995",
      "ageOnDepartureDate": 31,
      "passengerType": "Adult"
    },
    {
      "rowNumber": 3,
      "fullName": "Nguyen Van C",
      "dateOfBirth": "21/06/2018",
      "ageOnDepartureDate": 7,
      "passengerType": "Child"
    }
  ]
}
```

Nếu `canConfirm = false`, FE phải cho khách sửa file hoặc sửa dữ liệu trước khi gọi `PUT /passengers`.

Header bắt buộc:

```text
FullName,DateOfBirth
```

Các cột khác trong file như giới tính, số điện thoại, email, địa chỉ, ghi chú sẽ bị bỏ qua.

### 15. Lưu passenger manifest

```http
PUT /api/custom-booking-requests/{id}/passengers
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/passengers" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -H "Content-Type: application/json" \
  -d '{
    "passengers": [
      {
        "fullName": "Nguyen Van A",
        "dateOfBirth": "20/06/1995"
      },
      {
        "fullName": "Nguyen Van C",
        "dateOfBirth": "21/06/2018"
      }
    ]
  }'
```

Response mẫu:

```json
{
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "status": "Completed",
  "requiredPassengerCount": 2,
  "requiredAdultCount": 1,
  "requiredChildCount": 1,
  "passengerCount": 2,
  "adultCount": 1,
  "childCount": 1,
  "completedAt": "2026-06-18T03:30:00+00:00",
  "passengers": [
    {
      "passengerOrder": 1,
      "fullName": "Nguyen Van A",
      "passengerType": "Adult",
      "dateOfBirth": "20/06/1995",
      "ageOnDepartureDate": 31
    },
    {
      "passengerOrder": 2,
      "fullName": "Nguyen Van C",
      "passengerType": "Child",
      "dateOfBirth": "21/06/2018",
      "ageOnDepartureDate": 7
    }
  ]
}
```

Rule quan trọng:

- `PUT` thay thế toàn bộ danh sách cũ.
- Chỉ Customer chủ booking, Admin, hoặc Manager được giao booking được lưu.
- Staff chỉ được xem, không được lưu.
- Tổng hành khách, số adult, số child phải khớp số đã đăng ký.
- Dưới 11 tuổi tại ngày khởi hành là `Child`; từ đủ 11 tuổi là `Adult`.
- Sau khi check-in thành công, manifest bị `Locked` và không sửa được nữa.

### 16. Admin giao Manager sau khi booking Confirmed

Xem Manager tại bến khởi hành:

```http
GET /api/custom-booking-requests/{id}/manager-candidates
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/manager-candidates" \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

Response mẫu:

```json
[
  {
    "userId": "MANAGER_USER_ID",
    "fullName": "Nguyen Manager",
    "phoneNumber": "0900000000",
    "email": "manager@gmail.com",
    "stationId": "FROM_STATION_ID",
    "stationCode": "BACHDANG",
    "stationName": "Bạch Đằng",
    "isPrimaryStation": true
  }
]
```

Giao Manager:

```http
PUT /api/custom-booking-requests/{id}/assigned-manager
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/assigned-manager" \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  -H "Content-Type: application/json" \
  -d '{
    "managerUserId": "'"$MANAGER_USER_ID"'"
  }'
```

Nếu đổi Manager, backend xóa kế hoạch Staff/dịch vụ vận hành cũ để Manager mới lập lại.

### 17. Manager phân Staff và dịch vụ vận hành

Xem Staff tại bến khởi hành:

```http
GET /api/custom-booking-requests/{id}/staff-candidates
```

```bash
curl "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/staff-candidates" \
  -H "Authorization: Bearer $TOKEN_MANAGER"
```

Response mẫu:

```json
[
  {
    "userId": "STAFF_USER_ID",
    "fullName": "Nguyen Staff",
    "phoneNumber": "0911111111",
    "email": "staff@gmail.com",
    "isPrimaryStation": true
  }
]
```

Lưu operation plan:

```http
PUT /api/custom-booking-requests/{id}/operation-plan
```

```bash
curl -X PUT "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/operation-plan" \
  -H "Authorization: Bearer $TOKEN_MANAGER" \
  -H "Content-Type: application/json" \
  -d '{
    "staffAssignments": [
      {
        "staffUserId": "'"$STAFF_USER_ID"'",
        "dutyNote": "Đón khách và kiểm tra danh sách hành khách."
      }
    ],
    "services": [
      {
        "serviceName": "Trang trí sinh nhật",
        "quantity": 1,
        "note": "Thực hiện theo báo giá đã xác nhận."
      }
    ]
  }'
```

`PUT /operation-plan` thay thế toàn bộ kế hoạch hiện tại.

### 18. Admin hoặc Manager cấp lại QR khi sự cố

```http
POST /api/custom-booking-requests/{id}/ticket/reissue
```

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/ticket/reissue" \
  -H "Authorization: Bearer $TOKEN_MANAGER" \
  -H "Content-Type: application/json" \
  -d '{
    "reason": "QR không quét được tại cổng check-in."
  }'
```

Response mẫu:

```json
{
  "id": "00000000-0000-0000-0000-000000000020",
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "ticketCode": "CBT-20260618-ABC12345",
  "status": "Active",
  "qrToken": "new-raw-token",
  "qrPayload": "swb:custom-booking:new-raw-token",
  "qrIssuedAt": "2026-06-18T04:00:00+00:00",
  "qrExpiresAt": "2026-06-20T04:15:00+00:00",
  "qrUsedAt": null
}
```

Điều kiện:

- Chỉ Admin hoặc Manager được giao booking.
- Booking đã `Confirmed`.
- Vé đang `Active`, chưa dùng, chưa hết hạn.
- Có `reason`, tối đa 500 ký tự.
- QR cũ mất hiệu lực.
- Backend ghi audit log.

### 19. Staff/Admin/Manager scan QR check-in

```http
POST /api/custom-booking-requests/tickets/scan
```

Gửi JSON object:

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/tickets/scan" \
  -H "Authorization: Bearer $TOKEN_STAFF" \
  -H "Content-Type: application/json" \
  -d '{
    "qrToken": "swb:custom-booking:raw-token"
  }'
```

Hoặc gửi text/plain:

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/tickets/scan" \
  -H "Authorization: Bearer $TOKEN_STAFF" \
  -H "Content-Type: text/plain" \
  --data "swb:custom-booking:raw-token"
```

Response thành công:

```json
{
  "ticketId": "00000000-0000-0000-0000-000000000020",
  "customBookingRequestId": "CUSTOM_BOOKING_ID",
  "ticketCode": "CBT-20260618-ABC12345",
  "status": "Used",
  "qrUsedAt": "2026-06-20T01:05:00+00:00",
  "message": "Check-in vé thành công."
}
```

Điều kiện scan:

- Actor là Admin, Manager hoặc Staff.
- QR hợp lệ.
- Booking `Confirmed`.
- Manifest `Completed`.
- Chỉ scan từ 30 phút trước giờ khởi hành.
- Vé chưa dùng và chưa hết hạn.
- Scan thành công chuyển vé sang `Used` và manifest sang `Locked`.

Các lỗi nghiệp vụ thường gặp:

```json
{ "errors": { "qrToken": ["Danh sách hành khách chưa hoàn tất."] } }
```

```json
{ "errors": { "qrToken": ["Chưa đến thời gian check-in."] } }
```

```json
{ "errors": { "qrToken": ["Vé này đã được sử dụng."] } }
```

### 20. Khách hoặc Admin hủy booking

```http
POST /api/custom-booking-requests/{id}/cancel
```

```bash
curl -X POST "$BASE_URL/api/custom-booking-requests/$CUSTOM_BOOKING_ID/cancel" \
  -H "Authorization: Bearer $TOKEN_CUSTOMER" \
  -H "Content-Type: application/json" \
  -d '{
    "reason": "Không phù hợp ngân sách."
  }'
```

Điều kiện:

- Customer là chủ booking hoặc Admin.
- Chỉ hủy khi `PendingReview` hoặc `Quoted`.
- Manager/Staff không được hủy bằng endpoint này.

Response mẫu:

```json
{
  "id": "CUSTOM_BOOKING_ID",
  "status": "Cancelled",
  "statusReason": "Không phù hợp ngân sách."
}
```

## Thứ tự test nhanh end-to-end

```text
Customer:
1. GET /pricing-options
2. POST /
3. GET /{id}

Admin:
4. GET /{id}/vessel-candidates
5. PUT /{id}/assigned-vessel
6. POST /{id}/quote

Customer:
7. POST /{id}/accept-quote
8. POST /{id}/passengers/import/preview
9. PUT /{id}/passengers
10. GET /{id}/ticket

Admin:
11. GET /{id}/manager-candidates
12. PUT /{id}/assigned-manager

Manager:
13. GET /{id}/staff-candidates
14. PUT /{id}/operation-plan

Staff:
15. POST /tickets/scan
```

## Luồng khách hàng

### 1. Xem giá tham khảo

```http
GET /api/custom-booking-requests/pricing-options
    ?requestedNumberOfDecks=2
    &requestedSeatSetupType=StandardAndVip
    &rentalUnit=Hour
    &passengerCount=20
```

API chỉ trả:

- Số tàu đang phù hợp.
- Khoảng giá thuê theo `rentalUnit` khách chọn, nhóm theo currency.
- Không trả `vesselId`, tên tàu hoặc cho khách chọn tàu.

Giá này được lấy từ tàu:

- `Active`.
- Đã setup ghế.
- Đúng số tầng.
- Đúng kiểu ghế.
- Đủ sức chứa.
- Có giá thuê theo `rentalUnit` khách chọn.

Đây là khoảng giá trước khi Admin gán tàu cụ thể.

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
  "rentalUnit": "Hour",
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
- `rentalUnit` là `Hour` hoặc `Day`; backend dùng giá theo đơn vị này để tự tính báo giá.

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
- `quote.serviceFeeAmount`.
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

Sau khi booking `Confirmed`, khách bổ sung danh sách người lên tàu:

```http
GET /api/custom-booking-requests/{id}/passengers
PUT /api/custom-booking-requests/{id}/passengers
POST /api/custom-booking-requests/{id}/passengers/import/preview
```

Flow chuẩn:

1. Khách nhập tay hoặc upload file.
2. Upload file chỉ gọi `import/preview`, backend đọc file, tự tính thống kê và trả lỗi/cảnh báo tổng; không lưu DB.
3. Frontend hiển thị bảng preview cho khách kiểm tra/sửa.
4. Khi khách xác nhận, frontend gọi `PUT /passengers` để lưu manifest.

`PUT` nhận JSON, `import/preview` nhận `multipart/form-data` field `file` với `.csv` hoặc `.xlsx`.
File upload chỉ cần header bắt buộc:

```text
FullName | DateOfBirth
```

Các cột khác trong file như giới tính, số điện thoại, email, địa chỉ, ghi chú sẽ bị bỏ qua.

Rule manifest:

- Upload preview không thay đổi dữ liệu đang lưu.
- PUT thay thế toàn bộ danh sách hiện tại.
- Chỉ Customer chủ booking, Admin hoặc Manager được giao booking được cập nhật.
- Staff được xem danh sách để đối soát vận hành, không được cập nhật.
- Tổng số passenger phải đúng `adultCount + childCount`.
- Số `Adult` phải đúng `adultCount`, số `Child` phải đúng `childCount`.
- Tuổi tính tại `departureDate`, đủ ngày đủ tháng.
- Backend tự tính `Adult`/`Child` từ `DateOfBirth`; khách không cần nhập `PassengerType`.
- Dưới 11 tuổi là `Child`; từ đủ 11 tuổi là `Adult`.
- Cập nhật thành công sẽ đặt `passengerManifestStatus = Completed`.

Backend tạo một vé QR active cho custom booking nếu chưa có vé active:

- Lưu `qr_token` để khách có thể mở lại QR từ lịch sử booking.
- Lưu `qr_token_hash` để scan/verify token.
- Gửi confirmation email template có QR; frontend có thể gọi endpoint xem vé hoặc lịch sử booking để render lại `qrPayload`.
- Vé QR là vé dùng một lần; hệ thống không cấp token mới cho cùng vé sau khi đã phát hành.
- Nếu dữ liệu cũ chưa có `qr_token`, chỉ có thể backfill bằng token gốc nếu còn giữ từ email/frontend cũ; không thể dựng ngược token từ hash.

Khách có thể mở lại QR từ lịch sử booking bằng endpoint xem vé. Admin và Manager được giao booking cũng nhận `qrPayload` để hỗ trợ sự cố. Staff chỉ nhận metadata vé và chỉ dùng endpoint scan/check-in, không được lấy hoặc cấp lại QR.

Khi QR gặp sự cố tại check-in, Admin hoặc Manager được giao booking gọi:

```http
POST /api/custom-booking-requests/{id}/ticket/reissue
```

```json
{
  "reason": "QR không quét được tại cổng check-in."
}
```

Điều kiện cấp lại:

- Booking đã `Confirmed`.
- Vé đang `Active`, chưa dùng và chưa hết hạn.
- Có lý do sự cố.
- QR cũ mất hiệu lực vì backend thay token/hash mới.
- Ghi audit log kèm actor và lý do.

Khi scan/check-in QR:

- `passengerManifestStatus` phải là `Completed`.
- Chỉ cho check-in từ 30 phút trước giờ khởi hành.
- Nếu quét trước thời gian check-in, backend trả lỗi và vé vẫn giữ `Active`.
- Nếu scan thành công, vé chuyển sang `Used`.
- Nếu scan thành công, manifest chuyển sang `Locked`.
- Nếu vé đã quá hạn, vé chuyển sang `Expired`.

### 6. Hủy hoặc từ chối báo giá

```http
POST /api/custom-booking-requests/{id}/cancel
```

```json
{
  "reason": "Không phù hợp ngân sách."
}
```

Customer chủ booking hoặc Admin hủy được khi status là `PendingReview` hoặc `Quoted`.

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
- Có giá thuê theo `rentalUnit` khách chọn.

`estimatedBasePrice` được tính:

```text
billingQuantity =
  Hour: max(1, estimatedDurationMinutes) / 60
  Day: max(1, ceil(estimatedDurationMinutes / 1440))
estimatedBasePrice = round(unitPrice * billingQuantity, 2)
```

Đây là giá hệ thống sẽ dùng làm báo giá sau khi Admin gán tàu.

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
  "depositPercent": 50,
  "serviceFeeAmount": 400000,
  "priceNote": "Giá được hệ thống tính theo tàu và đơn vị thuê khách đã chọn."
}
```

Backend tính:

```text
baseVesselPrice = round(unitPrice của tàu theo rentalUnit * billingQuantity, 2)
quotedPrice = baseVesselPrice + serviceFeeAmount
depositAmount = round(quotedPrice * depositPercent / 100)
remainingAmount = quotedPrice - depositAmount
validUntil = min(thời điểm báo giá + 24 giờ, thời điểm khởi hành)
```

Admin không nhập `quotedPrice`, `currency` hoặc `validUntil`. Backend tự lấy giá/currency từ tàu, cộng `serviceFeeAmount`, tự đặt thời hạn và trả trong `quote`. Nếu không tính phụ phí thì bỏ `serviceFeeAmount` hoặc gửi `0`.
Admin chỉ báo giá được sau khi gán tàu. Có thể cập nhật báo giá khi status đang là `Quoted`.

### 5. Hủy yêu cầu

Admin dùng cùng endpoint:

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
| Customer | Tạo, sửa khi chưa gán tàu, xem booking/QR của mình, chấp nhận hoặc hủy báo giá, nhập/upload danh sách hành khách |
| Admin | Xem tất cả, chọn tàu, báo giá, giao Manager tại bến khởi hành, cấp lại QR khi có sự cố |
| Manager | Chỉ xem booking/QR được giao, chọn Staff, lập kế hoạch dịch vụ vận hành, nhập/upload danh sách hành khách, cấp lại QR khi có sự cố |
| Staff | Chỉ xem booking metadata/danh sách hành khách được Manager phân công và scan/check-in QR; không được lấy hoặc cấp lại QR |

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

- Chưa liên kết sang `Trip`; hiện chỉ kiểm tra trùng tàu giữa các custom booking có khung giờ giữ tàu.
- Chưa tạo payment.
- Chưa tự tạo trip/lịch chạy sau `Confirmed`.
- Chưa có catalog dịch vụ tiện ích và đơn giá riêng; dịch vụ vận hành hiện lưu theo từng booking.
