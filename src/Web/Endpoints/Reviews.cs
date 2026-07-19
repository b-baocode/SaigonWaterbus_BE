using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Reviews;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Reviews : IEndpointGroup
{
    public static string RoutePrefix => "/api/reviews";

    private const string CreateExample =
        """
        {
          "rating": 5,
          "comment": "Chuyen di rat tuyet, tau sach va dung gio."
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CreateTripReview, "trips/{tripId:guid}")
            .RequireAuthorization()
            .WithSummary("Danh gia mot chuyen da hoan thanh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token (khach hang co ve tren chuyen)",
                CreateExample,
                "Chi danh gia duoc chuyen tripStatus=Completed ma minh co booking Confirmed/Completed (chieu di, chieu ve hoac charter).",
                "rating: 1-5. comment: optional, toi da 1000 ky tu.",
                "Moi khach chi danh gia 1 lan/chuyen, khong sua duoc sau khi gui.",
                "Review moi tao o status=Hidden (cho duyet), chi hien thi public sau khi admin chuyen sang Published."));

        group.MapGet(GetMyReviewableTrips, "my/reviewable-trips")
            .RequireAuthorization()
            .WithSummary("Cac chuyen toi co the danh gia / da danh gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve cac chuyen da hoan thanh ma toi co ve, moi nhat truoc.",
                "myReview=null nghia la chua danh gia -> hien nut 'Danh gia'; nguoc lai hien noi dung da gui kem status (Hidden = cho duyet).",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100)."));

        group.MapGet(GetTripReviews, "trips/{tripId:guid}")
            .WithSummary("Danh sach danh gia cua mot chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public",
                null,
                "Chi tra ve review status=Published, moi nhat truoc, kem averageRating (lam tron 1 chu so).",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100)."));

        group.MapGet(GetRouteReviews, "routes/{routeId:guid}")
            .WithSummary("Danh sach danh gia cua mot tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public",
                null,
                "Gop review Published cua tat ca chuyen thuoc tuyen, kem averageRating - dung hien diem tuyen o man chon tuyen.",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100)."));

        group.MapGet(GetAdminReviews, "admin")
            .RequireAuthorization()
            .WithSummary("Quan tri: danh sach toan bo danh gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Query (tat ca optional): status (Published|Hidden - loc status=Hidden de xem hang cho duyet), rating (1-5), tripId, routeId, page, pageSize.",
                "Tra ve kem thong tin khach, bookingCode, tripCode, tuyen de doi soat."));

        group.MapPatch(UpdateReviewStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Quan tri: duyet (hien) / an mot danh gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                """{ "status": "Published" }""",
                "status: Published (duyet, cho hien public) | Hidden (an khoi danh sach public - cung la trang thai mac dinh khi vua tao). Khong xoa cung du lieu.",
                "Idempotent: gui lai status hien tai thi giu nguyen."));
    }

    private static async Task<IResult> CreateTripReview(
        ISender sender,
        Guid tripId,
        CreateTripReviewRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(
            new CreateTripReviewCommand(tripId, request.Rating, request.Comment), ct));

    private static async Task<IResult> GetMyReviewableTrips(
        ISender sender,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetMyReviewableTripsQuery(page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> GetTripReviews(
        ISender sender,
        Guid tripId,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripReviewsQuery(tripId, page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> GetRouteReviews(
        ISender sender,
        Guid routeId,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteReviewsQuery(routeId, page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> GetAdminReviews(
        ISender sender,
        string? status,
        int? rating,
        Guid? tripId,
        Guid? routeId,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetAdminReviewListQuery(
            status, rating, tripId, routeId, page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> UpdateReviewStatus(
        ISender sender,
        Guid id,
        UpdateReviewStatusRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateReviewStatusCommand(id, request.Status), ct));

    public sealed record CreateTripReviewRequest(int Rating, string? Comment);

    public sealed record UpdateReviewStatusRequest(string Status);
}
