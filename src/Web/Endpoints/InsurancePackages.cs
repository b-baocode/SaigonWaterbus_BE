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
          "name": "Bao hiem hanh khach thue tau",
          "bookingType": "CharterBooking",
          "unitPremiumAmount": 10000,
          "coverageAmount": 50000000,
          "isRequired": false,
          "providerName": "Bao hiem mac dinh",
          "providerLogoUrl": "https://cdn.example.com/insurance/logo.png",
          "conditions": [
            "Chi ap dung cho hanh khach co ten trong danh sach chuyen di.",
            "Chi co hieu luc trong thoi gian dien ra chuyen thue tau."
          ],
          "termsUrl": "https://cdn.example.com/insurance/terms.pdf",
          "status": "Active",
          "displayOrder": 1
        }
        """;

    private const string UpdateExample =
        """
        {
          "name": "Bao hiem hanh khach thue tau",
          "bookingType": "CharterBooking",
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
          "status": "Active",
          "displayOrder": 1
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
                "Query params: bookingType optional (SeatBooking | CharterBooking), activeOnly optional true/false.",
                "FE charter booking goi: GET /api/insurance-packages?bookingType=CharterBooking",
                "providerLogoUrl la link logo bao hiem de FE hien thi."));

        group.MapPost(CreateInsurancePackage, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao goi bao hiem")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                CreateExample,
                "bookingType hop le: SeatBooking | CharterBooking.",
                "code unique theo bookingType.",
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
            request.Conditions,
            request.TermsUrl,
            request.Status,
            request.DisplayOrder), ct));

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
        IReadOnlyList<string>? Conditions,
        string? TermsUrl,
        InsurancePackageStatus Status,
        int DisplayOrder);

    public sealed record UpdateInsurancePackageStatusRequest(InsurancePackageStatus Status);
}
