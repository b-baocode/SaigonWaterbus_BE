# Huong dan implement backend dung chuan

Tai lieu nay huong dan cach them nghiep vu moi vao skeleton hien tai theo Clean Architecture.

## 1. Nguyen tac bat buoc

1. Domain khong phu thuoc Application, Infrastructure, Web.
2. Application chi lam use-case, khong chua chi tiet ha tang.
3. Infrastructure chi chua phan ket noi DB, external service, persistence.
4. Web chi chua endpoint, mapping request/response, khong viet business logic.
5. Moi thay doi nghiep vu phai co test tuong ung.

## 2. Cau truc thu muc de them nghiep vu

Vi du nghiep vu `Routes`:

- `src/Domain/Entities/Route.cs`
- `src/Application/Routes/Commands/CreateRoute/CreateRoute.cs`
- `src/Application/Routes/Queries/GetRoutes/GetRoutes.cs`
- `src/Web/Endpoints/Routes.cs`
- `src/Infrastructure/Data/Configurations/RouteConfiguration.cs`

## 3. Quy trinh implement tung buoc

### Buoc 1: Domain truoc

1. Tao entity/value object trong Domain.
2. Dat rule nghiep vu trong entity (validation noi tai, invariant).
3. Khong tham chieu den EF Core, MediatR, HTTP.

### Buoc 2: Application use-case

1. Moi use-case la 1 command hoac query rieng.
2. Validator di kem command/query neu can.
3. Handler chi lam nghiep vu use-case, goi `IApplicationDbContext` de thao tac du lieu.
4. Khong viet SQL tay trong Application.

### Buoc 3: Infrastructure persistence

1. Them `DbSet` vao `ApplicationDbContext` neu can.
2. Tao EF configuration trong `src/Infrastructure/Data/Configurations`.
3. Neu thay doi schema, tao migration.

### Buoc 4: Web endpoint

1. Tao endpoint group trong `src/Web/Endpoints`.
2. Endpoint chi nhan request, goi MediatR, tra ket qua.
3. Khong dat logic nghiep vu trong endpoint.

### Buoc 5: Test

1. Unit test cho validator va logic can tach rieng.
2. Integration/functional test cho flow quan trong.
3. Chay `dotnet build` va `dotnet test` truoc khi merge.

## 4. Coding conventions de giu code gon

1. Mot file command/query nen chua:
   - Record request
   - Validator (neu co)
   - Handler
2. Dat ten ro rang:
   - `CreateXCommand`, `UpdateXCommand`, `DeleteXCommand`
   - `GetXQuery`, `GetXByIdQuery`
3. Tranh class qua dai. Tach helper neu file > 200 dong.
4. Logging chi ghi thong tin can thiet, khong log du lieu nhay cam.

## 5. Checklist truoc khi commit

- [ ] Khong dat business logic o Web.
- [ ] Khong dat EF-specific logic o Domain.
- [ ] Da co validator cho input quan trong.
- [ ] Da cap nhat mapping/configuration EF.
- [ ] `dotnet build` pass.
- [ ] `dotnet test` pass.
- [ ] Da cap nhat tai lieu API neu co endpoint moi.

## 6. Mau minimal cho command

```csharp
public record CreateRouteCommand(string Code, string Name) : IRequest<Guid>;

public class CreateRouteCommandHandler : IRequestHandler<CreateRouteCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreateRouteCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        var entity = new Route
        {
            Code = request.Code,
            Name = request.Name
        };

        _context.Set<Route>().Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }
}
```

## 7. Luu y cho repo hien tai

1. Repo dang o che do khong auth. Neu can auth sau nay, add lai theo module rieng, khong chen truc tiep vao tat ca layer.
2. Repo da bo nghiep vu mau. Chi them module that su can cho do an.
3. Uu tien implement theo chieu doc: Domain -> Application -> Infrastructure -> Web -> Test.
