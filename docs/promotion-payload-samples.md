# Payload mẫu tạo Promotion — `POST /api/promotions`

Dùng cho Swagger UI (Authorize bằng Bearer token có role Admin trước khi Try it out).

Field chung bắt buộc: `promotionCode`, `promotionName`, `promotionType`, `discountValue`,
`validFrom`, `validTo`. Các field còn lại optional (null = không giới hạn).

Enum hợp lệ:
- `promotionType`: `Percent` | `Fixed`
- `visibility`: `Public` | `Private`
- `status`: `Draft` | `Active` | `Paused` (không tạo mới với `Archived`)
- `scope.bookingTypes`: `SeatBooking` | `CharterBooking`
- `scope.daysOfWeek`: **chuỗi tên** của `System.DayOfWeek` — `"Sunday"`, `"Monday"`, …, `"Saturday"` (API cấu hình `JsonStringEnumConverter(allowIntegerValues: false)`, gửi số như `1` sẽ bị 400 "Failed to read parameter... as JSON").

Ràng buộc cần nhớ:
- `discountValue` phải > 0; nếu `promotionType = Percent` thì ≤ 100.
- `maxDiscountAmount` **chỉ dùng với `Percent`** — gửi cùng `Fixed` sẽ bị 400.
- `validTo` phải sau `validFrom`.
- `promotionCode` tự động uppercase, phải unique.

---

## 1. Percent — giảm % có trần, áp mọi nơi, công khai

```json
{
  "promotionCode": "WELCOME10",
  "promotionName": "Chao mung khach moi",
  "description": "Giam 10% cho khach dat ve lan dau, toi da 30.000d.",
  "promotionType": "Percent",
  "discountValue": 10,
  "maxDiscountAmount": 30000,
  "minOrderValue": 20000,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": 1000,
  "maxUsesPerAccount": 1,
  "budgetCap": null,
  "firstBookingOnly": true,
  "scope": null,
  "visibility": "Public",
  "status": "Active",
  "imageUrl": null
}
```

## 2. Fixed — giảm số tiền cố định, không trần (không gửi maxDiscountAmount)

```json
{
  "promotionCode": "SALE50K",
  "promotionName": "Giam 50k don tu 300k",
  "description": "Giam thang 50.000d cho don hang tu 300.000d.",
  "promotionType": "Fixed",
  "discountValue": 50000,
  "maxDiscountAmount": null,
  "minOrderValue": 300000,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": 500,
  "maxUsesPerAccount": 2,
  "budgetCap": 25000000,
  "firstBookingOnly": false,
  "scope": null,
  "visibility": "Public",
  "status": "Active",
  "imageUrl": null
}
```

## 3. Scope theo tuyến (routeIds) — chỉ áp dụng cho một số tuyến cụ thể

```json
{
  "promotionCode": "ROUTE-BD-TD",
  "promotionName": "Uu dai tuyen Bach Dang - Thu Duc",
  "description": "Giam 15% rieng tuyen Bach Dang - Thu Duc.",
  "promotionType": "Percent",
  "discountValue": 15,
  "maxDiscountAmount": 40000,
  "minOrderValue": null,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2027-06-30T23:59:59+07:00",
  "usageLimit": null,
  "maxUsesPerAccount": null,
  "budgetCap": null,
  "firstBookingOnly": false,
  "scope": {
    "bookingTypes": ["SeatBooking"],
    "routeIds": [
      "00000000-0000-0000-0000-000000000001",
      "00000000-0000-0000-0000-000000000002"
    ],
    "daysOfWeek": null,
    "departureFrom": null,
    "departureTo": null
  },
  "visibility": "Public",
  "status": "Active",
  "imageUrl": null
}
```

## 4. Scope off-peak — theo thứ trong tuần + khung giờ khởi hành

```json
{
  "promotionCode": "OFFPEAK-WEEKDAY",
  "promotionName": "Gio thap diem T2-T6",
  "description": "Giam 20% cho chuyen khoi hanh 9h-15h cac ngay trong tuan.",
  "promotionType": "Percent",
  "discountValue": 20,
  "maxDiscountAmount": 25000,
  "minOrderValue": null,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": null,
  "maxUsesPerAccount": null,
  "budgetCap": null,
  "firstBookingOnly": false,
  "scope": {
    "bookingTypes": null,
    "routeIds": null,
    "daysOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "departureFrom": "09:00:00",
    "departureTo": "15:00:00"
  },
  "visibility": "Public",
  "status": "Active",
  "imageUrl": null
}
```

## 5. Charter-only — chỉ áp dụng cho thuê trọn tàu (CharterBooking)

```json
{
  "promotionCode": "CHARTER-VIP",
  "promotionName": "Uu dai thue tron tau",
  "description": "Giam 500.000d cho don thue tron tau tu 5.000.000d.",
  "promotionType": "Fixed",
  "discountValue": 500000,
  "maxDiscountAmount": null,
  "minOrderValue": 5000000,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": 200,
  "maxUsesPerAccount": 1,
  "budgetCap": 100000000,
  "firstBookingOnly": false,
  "scope": {
    "bookingTypes": ["CharterBooking"],
    "routeIds": null,
    "daysOfWeek": null,
    "departureFrom": null,
    "departureTo": null
  },
  "visibility": "Public",
  "status": "Active",
  "imageUrl": null
}
```

## 6. Private — mã bí mật gửi riêng (không hiện ở trang khuyến mãi công khai)

```json
{
  "promotionCode": "VIP-SECRET25",
  "promotionName": "Uu dai rieng khach VIP",
  "description": "Giam 25%, toi da 100.000d. Chi gui qua email cho khach VIP.",
  "promotionType": "Percent",
  "discountValue": 25,
  "maxDiscountAmount": 100000,
  "minOrderValue": null,
  "validFrom": "2026-07-01T00:00:00+07:00",
  "validTo": "2026-09-30T23:59:59+07:00",
  "usageLimit": 50,
  "maxUsesPerAccount": 1,
  "budgetCap": null,
  "firstBookingOnly": false,
  "scope": null,
  "visibility": "Private",
  "status": "Active",
  "imageUrl": null
}
```

## 7. Draft — soạn trước, chưa phát hành (chưa áp dụng được)

```json
{
  "promotionCode": "SUMMER2026",
  "promotionName": "Khuyen mai he 2026",
  "description": "Dang soan, se kich hoat truoc mua he.",
  "promotionType": "Percent",
  "discountValue": 12,
  "maxDiscountAmount": 35000,
  "minOrderValue": 50000,
  "validFrom": "2026-05-01T00:00:00+07:00",
  "validTo": "2026-08-31T23:59:59+07:00",
  "usageLimit": null,
  "maxUsesPerAccount": null,
  "budgetCap": null,
  "firstBookingOnly": false,
  "scope": null,
  "visibility": "Public",
  "status": "Draft",
  "imageUrl": null
}
```

---

# Payload mẫu cập nhật — `PUT /api/promotions/{id}`

Lưu ý: **không gửi `promotionCode` và `promotionType`** — hai field này khoá cứng sau khi tạo,
không có trong `UpdatePromotionRequest`.

## 8. Update — mở rộng ưu đãi, đổi ngân sách, giữ nguyên loại

```json
{
  "promotionName": "Chao mung khach moi (mo rong)",
  "description": "Giam 15%, toi da 50.000d.",
  "discountValue": 15,
  "maxDiscountAmount": 50000,
  "minOrderValue": 30000,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": 2000,
  "maxUsesPerAccount": 2,
  "budgetCap": 5000000,
  "firstBookingOnly": false,
  "scope": null,
  "visibility": "Public",
  "status": "Active"
}
```

## 9. Update — tạm ngưng khuyến mãi (Paused)

```json
{
  "promotionName": "Chao mung khach moi",
  "description": "Tam ngung de xem lai ngan sach.",
  "discountValue": 10,
  "maxDiscountAmount": 30000,
  "minOrderValue": 20000,
  "validFrom": "2026-01-01T00:00:00+07:00",
  "validTo": "2026-12-31T23:59:59+07:00",
  "usageLimit": 1000,
  "maxUsesPerAccount": 1,
  "budgetCap": null,
  "firstBookingOnly": true,
  "scope": null,
  "visibility": "Public",
  "status": "Paused"
}
```

---

# Ghi chú nhanh

- Xoá khuyến mãi: `DELETE /api/promotions/{id}` → soft delete, set `status = Archived`, không bật lại được.
- Kiểm tra mã trước khi áp: `GET /api/promotions/validate?code=WELCOME10&subtotalAmount=40000`.
- Upload ảnh riêng: `PUT /api/promotions/{id}/image` (multipart field `image`, hoặc JSON `{ "imageUrl": "..." }`; imageUrl rỗng/không gửi = xoá ảnh).
