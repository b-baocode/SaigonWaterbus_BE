using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class InsurancePackages : IEndpointGroup
{
    public static string RoutePrefix => "/api/insurance-packages";

    private const string CreateExample =
        """
        {
          "code": "CHARTER_PASSENGER_BASIC",
          "name": "Bao hiem hanh khach",
          "bookingType": "PassengerInsurance",
          "unitPremiumAmount": 10000,
          "coverageAmount": 50000000,
          "isRequired": false,
          "providerName": "Bao hiem mac dinh",
          "providerLogoUrl": "https://cdn.example.com/insurance/logo.png",
          "conditions": [
            "Chi ap dung cho hanh khach co ten trong danh sach chuyen di.",
            "Chi co hieu luc trong thoi gian dien ra chuyen di."
          ],
          "termsUrl": "https://cdn.example.com/insurance/terms.pdf",
          "status": "Active"
        }
        """;

    private const string UpdateExample =
        """
        {
          "name": "Bao hiem hanh khach",
          "bookingType": "PassengerInsurance",
          "unitPremiumAmount": 12000,
          "coverageAmount": 50000000,
          "isRequired": false,
          "providerName": "Bao hiem mac dinh",
          "providerLogoUrl": "https://cdn.example.com/insurance/logo.png",
          "conditions": [
            "Chi ap dung cho hanh khach co ten trong danh sach chuyen di.",
            "Chi co hieu luc trong thoi gian dien ra chuyen thue tau."
          ],
          "termsUrl": "https://cdn.example.com/insurance/terms.pdf",
          "status": "Active"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "status": "Inactive"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetInsurancePackages, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: bookingType optional, activeOnly optional true/false.",
                "Logic moi chi dung mot loai bao hiem theo hanh khach: bookingType=PassengerInsurance.",
                "FE co the goi GET /api/insurance-packages?bookingType=PassengerInsurance hoac bo bookingType.",
                "Query SeatBooking/CharterBooking cu van duoc ho tro tam thoi va se tra them goi PassengerInsurance de FE cu khong bi hong.",
                "providerLogoUrl la link logo bao hiem de FE hien thi."));

        group.MapPost(CreateInsurancePackage, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                CreateExample,
                "bookingType nen gui PassengerInsurance hoac bo trong; SeatBooking/CharterBooking chi giu de tuong thich request cu.",
                "code unique theo bookingType da normalize ve PassengerInsurance.",
                "unitPremiumAmount la gia tren moi hanh khach, VND.",
                "coverageAmount la muc boi thuong/quyen loi tren moi hanh khach."));

        group.MapPut(UpdateInsurancePackage, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateExample,
                "Code khong doi qua API update.",
                "status hop le: Active | Inactive."));

        group.MapPatch(UpdateInsurancePackageStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateStatusExample,
                "status hop le: Active | Inactive.",
                "Inactive se an goi khoi FE khi activeOnly=true."));

        group.MapPut(UpdateInsurancePackageImage, "{id:guid}/image")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithSummary("Cap nhat anh goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Gui multipart/form-data voi field 'image' chua file anh.",
                "Ho tro: jpeg, png, gif, webp. Dung luong toi da 5MB."));
    }

    private static async Task<IResult> GetInsurancePackages(
        ISender sender,
        string? bookingType,
        bool? activeOnly,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetInsurancePackageListQuery(
            bookingType,
            activeOnly ?? true), ct));

    private static async Task<IResult> CreateInsurancePackage(
        ISender sender,
        CreateInsurancePackageCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateInsurancePackage(
        ISender sender,
        Guid id,
        UpdateInsurancePackageRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateInsurancePackageCommand(
            id,
            request.Name,
            request.BookingType,
            request.UnitPremiumAmount,
            request.CoverageAmount,
            request.IsRequired,
            request.ProviderName,
            request.ProviderLogoUrl,
            request.ImageUrl,
            request.Conditions,
            request.TermsUrl,
            request.Status,
            request.RewardOption,
            request.ProviderSource), ct));

    private static async Task<IResult> UpdateInsurancePackageImage(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        UpdateInsurancePackageImageCommand? command = null;
        try
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault(f =>
                    string.Equals(f.Name, "image", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.Name, "file", StringComparison.OrdinalIgnoreCase));

                if (file is not null && file.Length > 0)
                {
                    var content = new MemoryStream();
                    await file.CopyToAsync(content, ct);
                    content.Position = 0;
                    command = new UpdateInsurancePackageImageCommand(
                        id,
                        new InsurancePackageImageFileRequest(file.FileName, file.ContentType, file.Length, content));
                    return Results.Ok(await sender.Send(command, ct));
                }
            }

            return Results.BadRequest(new { error = "Vui lòng gửi file ảnh (multipart/form-data với field 'image')." });
        }
        finally
        {
            command?.ImageFile?.Content.Dispose();
        }
    }

    private static async Task<IResult> UpdateInsurancePackageStatus(
        ISender sender,
        Guid id,
        UpdateInsurancePackageStatusRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateInsurancePackageStatusCommand(id, request.Status), ct));

    public sealed record UpdateInsurancePackageRequest(
        string Name,
        string BookingType,
        decimal UnitPremiumAmount,
        decimal CoverageAmount,
        bool IsRequired,
        string? ProviderName,
        string? ProviderLogoUrl,
        string? ImageUrl,
        IReadOnlyList<string>? Conditions,
        string? TermsUrl,
        InsurancePackageStatus Status,
        int? RewardOption,
        InsuranceProviderSource ProviderSource = InsuranceProviderSource.ThirdParty);

    public sealed record UpdateInsurancePackageStatusRequest(InsurancePackageStatus Status);
}
