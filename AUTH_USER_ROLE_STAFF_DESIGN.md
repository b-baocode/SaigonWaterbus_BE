# Auth, User, Role, Staff Profile Design

Tai lieu nay chot thiet ke 3 bang chinh cho auth va quan ly nguoi dung noi bo trong he thong Saigon Waterbus.

## 1. Muc tieu

- Giu auth gon, de quan ly, de mo rong.
- Khong de role bi no thanh qua nhieu loai nhan vien.
- Ho tro tot cho Customer, Staff, Manager, Operator, Admin.
- Dam bao co the phat trien tiep cac nghiep vu ban ve offline, quet QR, quan ly ben, quan ly tau, CSKH.

## 2. Chot 3 bang chinh

### 2.1 `roles`

Bang nay chi luu quyen truy cap cap cao.

| Cot | Kieu goi y | Rang buoc | Ghi chu |
| --- | --- | --- | --- |
| `id` | `int` | PK | Khoa chinh |
| `code` | `varchar(30)` | unique, not null | `Customer`, `Staff`, `Manager`, `Operator`, `Admin` |
| `name` | `varchar(100)` | not null | Ten hien thi |
| `created_at` | `timestamp` | not null | Audit |
| `updated_at` | `timestamp` | not null | Audit |

Gia tri role duoc chot:

- `Customer`
- `Staff`
- `Manager`
- `Operator`
- `Admin`

Nguyen tac:

- Role chi tra loi cau hoi: tai khoan duoc vao man hinh nao va duoc lam nhom thao tac nao.
- Khong dung role de bieu dien `TicketSeller`, `TicketInspector`, `BoatDriver`.

### 2.2 `users`

Bang nay la danh tinh dang nhap chung cho tat ca tai khoan.

| Cot | Kieu goi y | Rang buoc | Ghi chu |
| --- | --- | --- | --- |
| `id` | `int` | PK | Khoa chinh |
| `username` | `varchar(50)` | unique, not null | Dang nhap noi bo hoac cho customer |
| `email` | `varchar(150)` | unique, not null | Customer dang ky chi cho phep `gmail.com` hoac `fpt.edu.vn` |
| `password_hash` | `varchar(500)` | not null | Luu hash mat khau |
| `full_name` | `varchar(150)` | not null | Ho ten |
| `phone_number` | `varchar(20)` | null | So dien thoai |
| `role_id` | `int` | FK -> `roles.id`, not null | Role chinh |
| `status` | `varchar(20)` | not null | `Active`, `Locked`, `Inactive` |
| `token_version` | `int` | not null, default 1 | Dung de revoke token |
| `last_login_at` | `timestamp` | null | Lan dang nhap gan nhat |
| `created_at` | `timestamp` | not null | Audit |
| `updated_at` | `timestamp` | not null | Audit |

Nguyen tac:

- Tat ca account deu nam o day, bao gom Customer va nhan su noi bo.
- Customer co the chi can `users + role`.
- Tai khoan noi bo se co them `staff_profiles`.

### 2.3 `staff_profiles`

Bang nay chi danh cho nhan su noi bo.

| Cot | Kieu goi y | Rang buoc | Ghi chu |
| --- | --- | --- | --- |
| `user_id` | `int` | PK, FK -> `users.id` | Quan he 1-1 voi `users` |
| `staff_code` | `varchar(30)` | unique, not null | Ma nhan vien |
| `staff_type` | `varchar(30)` | not null | Loai cong viec |
| `scope_type` | `varchar(20)` | not null | `System`, `Station`, `Fleet`, `Boat` |
| `scope_id` | `int` | not null | Dinh danh pham vi phu trach |
| `manager_user_id` | `int` | FK -> `users.id`, null | Quan ly truc tiep neu can |
| `is_active` | `bool` | not null | Trang thai ho so nhan su |
| `created_at` | `timestamp` | not null | Audit |
| `updated_at` | `timestamp` | not null | Audit |

Gia tri `staff_type` de nghi:

- `TicketSeller`
- `TicketInspector`
- `BoatDriver`
- `BoatCrew`
- `CustomerService`
- `ContentOperator`
- `StationOperations`
- `FleetOperations`

Gia tri `scope_type` de nghi:

- `System`
- `Station`
- `Fleet`
- `Boat`

Nguyen tac:

- `Customer` khong co ban ghi trong `staff_profiles`.
- `Staff`, `Manager`, `Operator`, `Admin` nen co `staff_profiles`.
- `Manager` quan ly theo `scope_type + scope_id`, khong quan ly toan he thong.

## 3. Tai sao 3 bang nay la can bang tot nhat

- `roles` giu quyen lon, khong bi no.
- `users` giu identity chung, de auth va dang nhap.
- `staff_profiles` tach du lieu nhan su noi bo, tranh nhung cot chi co y nghia voi nhan vien bi nhat het vao `users`.

Neu chi dung 2 bang `users + roles`, bang `users` se bi pha tron giua:

- du lieu customer
- du lieu nhan vien
- du lieu to chuc noi bo

Dieu nay se kho mo rong khi them:

- nhan vien ban ve offline
- nhan vien quet QR
- nhan vien lai tau
- nhan vien CSKH
- nhan vien content

## 4. Rule nghiep vu chot

### 4.1 Dang ky va dang nhap

- Customer duoc tu dang ky.
- Staff, Manager, Operator, Admin khong duoc tu dang ky.
- Moi tai khoan deu dang nhap chung qua auth service.
- Khi login, he thong xac thuc `username/email + password`.
- Sau khi xac thuc, he thong dung `role + staff_type + scope` de phan quyen.

### 4.2 Tao tai khoan noi bo

- `Admin` tao duoc `Manager`, `Staff`, `Operator`.
- `Manager` chi tao duoc `Staff` trong dung `scope` cua minh.
- `Operator` khong tao account.
- `Customer` khong tao account noi bo.

### 4.3 Doi role va khoa tai khoan

- `Admin` duoc doi role va khoa/mo tai khoan.
- `Manager` khong duoc doi role.
- `Manager` co the khoa/mo `Staff` trong pham vi minh phu trach neu ban muon mo quyen nay.

## 5. Mapping quyen theo role

### `Customer`

- Dang ky
- Dang nhap
- Quan ly tai khoan ca nhan
- Tim lich, dat ve, thanh toan, nhan QR
- Xem lich su dat ve
- Danh gia chuyen di

### `Staff`

Phu thuoc `staff_type`.

Vi du:

- `TicketSeller`: ban ve offline, tao order tai quay
- `TicketInspector`: scan QR, verify ve, update boarding
- `BoatDriver`: xem chuyen duoc phan cong, xem danh sach hanh khach, bao incident

### `Manager`

- Tao va cap nhat `Staff`
- Xem dashboard cua station hoac fleet duoc giao
- Xu ly huy chuyen, delay, doi lich trong pham vi duoc giao
- Khong quan ly toan he thong

### `Operator`

- `CustomerService`: xu ly yeu cau khach hang, khiem nai, van de booking
- `ContentOperator`: quan ly noi dung, bai viet, banner

### `Admin`

- Quan ly toan he thong
- Quan ly user status
- Quan ly route, station, boat, schedule, fare, promotion, voucher
- Xem bao cao tong hop

## 6. Claims trong token

Access token nen co:

- `sub`
- `role`
- `staffType`
- `scopeType`
- `scopeId`
- `tokenVersion`

Nguyen tac:

- `role` quyet dinh quyen lon
- `staffType` quyet dinh nghiep vu cu the
- `scopeType + scopeId` quyet dinh duoc thao tac o dau

## 7. API de nghi

Dia chi API duoc chot ro rang theo nhom.

### 7.1 Public Auth API

| Method | API | Actor | Muc dich |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | Anonymous | Dang ky customer |
| `POST` | `/api/auth/login` | Anonymous | Dang nhap |
| `POST` | `/api/auth/refresh-token` | Da login | Lam moi token |
| `POST` | `/api/auth/logout` | Da login | Dang xuat |
| `GET` | `/api/auth/me` | Da login | Lay thong tin tai khoan hien tai |
| `POST` | `/api/auth/forgot-password` | Anonymous | Gui email reset mat khau |
| `POST` | `/api/auth/reset-password` | Anonymous | Dat lai mat khau |

### 7.2 Admin User Management API

| Method | API | Actor | Muc dich |
| --- | --- | --- | --- |
| `GET` | `/api/admin/users` | Admin | Danh sach tat ca account |
| `GET` | `/api/admin/users/{userId}` | Admin | Chi tiet account |
| `POST` | `/api/admin/internal-users` | Admin | Tao Manager, Staff, Operator |
| `PATCH` | `/api/admin/users/{userId}/status` | Admin | Khoa, mo, vo hieu hoa account |
| `PATCH` | `/api/admin/users/{userId}/role` | Admin | Doi role |
| `PATCH` | `/api/admin/users/{userId}/staff-profile` | Admin | Doi `staff_type`, `scope`, `manager_user_id` |
| `POST` | `/api/admin/users/{userId}/reset-password` | Admin | Reset mat khau noi bo |

### 7.3 Manager Staff Management API

| Method | API | Actor | Muc dich |
| --- | --- | --- | --- |
| `GET` | `/api/manager/staff` | Manager | Danh sach staff trong pham vi duoc giao |
| `GET` | `/api/manager/staff/{userId}` | Manager | Chi tiet staff trong scope |
| `POST` | `/api/manager/staff` | Manager | Tao Staff moi trong scope cua manager |
| `PATCH` | `/api/manager/staff/{userId}` | Manager | Sua thong tin staff trong scope |
| `PATCH` | `/api/manager/staff/{userId}/status` | Manager | Khoa mo staff trong scope neu mo quyen nay |
| `POST` | `/api/manager/staff/{userId}/reset-password` | Manager | Reset mat khau staff trong scope |

### 7.4 Self Service API

| Method | API | Actor | Muc dich |
| --- | --- | --- | --- |
| `GET` | `/api/account/profile` | Da login | Xem thong tin ca nhan |
| `PATCH` | `/api/account/profile` | Da login | Sua thong tin ca nhan |
| `POST` | `/api/account/change-password` | Da login | Doi mat khau |

## 8. Rule check role cho mot so nghiep vu quan trong

### Ban ve offline

- API de nghi: `POST /api/staff/offline-orders`
- Cho phep:
  - `role = Staff`
  - `staff_type = TicketSeller`
  - `scope_type = Station`

### Scan QR va len tau

- API de nghi: `POST /api/staff/tickets/scan`
- Cho phep:
  - `role = Staff`
  - `staff_type = TicketInspector`
  - `scope_type = Station` hoac `Boat`

### Dashboard tram / ben

- API de nghi: `GET /api/manager/dashboard`
- Cho phep:
  - `role = Manager`
  - du lieu tra ve duoc loc theo `scope_type + scope_id`

### CSKH

- API de nghi: `POST /api/operator/support-tickets/{ticketId}/reply`
- Cho phep:
  - `role = Operator`
  - `staff_type = CustomerService`

### Content

- API de nghi: `POST /api/operator/posts`
- Cho phep:
  - `role = Operator`
  - `staff_type = ContentOperator`

## 9. Chi so va rang buoc nen co

### `roles`

- unique index: `code`

### `users`

- unique index: `username`
- unique index: `email`
- index: `role_id`
- index: `status`

### `staff_profiles`

- unique index: `staff_code`
- unique index: `user_id`
- index: `manager_user_id`
- index tong hop: `scope_type, scope_id`
- index tong hop: `staff_type, scope_type, scope_id`

## 10. Pham vi cua 3 bang nay

Ba bang nay chi giai quyet:

- auth
- quan ly account
- quan ly nhan su noi bo
- phan quyen theo role, staff type, scope

Khong nen nhoi them cac nghiep vu ticketing vao day. Cac phan sau se la bang rieng:

- orders
- tickets
- schedules
- stations
- boats
- routes
- promotions
- reviews

## 11. Chot cuoi cung

Thiet ke duoc chot:

- `roles`
- `users`
- `staff_profiles`

Cong thuc auth va authorization:

- `users` = identity dang nhap
- `roles` = quyen lon
- `staff_profiles` = loai nhan su + pham vi phu trach

Cong thuc phan quyen:

- `role` + `staff_type` + `scope_type` + `scope_id`

Day la phuong an gon nhat nhung van du suc phat trien cho bai toan Saigon Waterbus.
