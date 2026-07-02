using SaigonWaterbus.Application.BlogPosts;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class BlogPosts : IEndpointGroup
{
    public static string RoutePrefix => "/api/blog-posts";

    private const string CreateExample =
        """
        {
          "title": "Kham pha Sai Gon bang waterbus",
          "slug": "kham-pha-sai-gon-bang-waterbus",
          "summary": "Nhung diem nen trai nghiem tren tuyen waterbus Sai Gon.",
          "category": "Activity",
          "imageUrl": "https://res.cloudinary.com/demo/image/upload/waterbus/blog-cover.webp",
          "imageAltText": "Tau waterbus tren song Sai Gon",
          "content": "Noi dung bai viet...",
          "status": "Draft"
        }
        """;

    private const string UpdateExample =
        """
        {
          "title": "Kham pha Sai Gon bang waterbus",
          "slug": "kham-pha-sai-gon-bang-waterbus",
          "summary": "Nhung diem nen trai nghiem tren tuyen waterbus Sai Gon.",
          "category": "News",
          "imageUrl": "https://res.cloudinary.com/demo/image/upload/waterbus/blog-cover.webp",
          "imageAltText": "Tau waterbus tren song Sai Gon",
          "content": "Noi dung bai viet da cap nhat...",
          "status": "Published"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPublishedBlogPosts, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach blog da xuat ban")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Chi tra ve bai viet co Status = Published.",
                "Sap xep theo PublishedAt moi nhat truoc."));

        group.MapGet(GetPublishedBlogPostBySlug, "{slug}")
            .AllowAnonymous()
            .WithSummary("Chi tiet blog da xuat ban theo slug")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve 404 neu slug khong ton tai hoac bai viet chua Published."));

        group.MapGet(GetBlogPostManagementList, "management")
            .RequireAuthorization()
            .WithSummary("Danh sach blog de quan ly")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Danh cho Admin, Manager, Staff.",
                "Query params: status (optional): Draft | Published | Archived.",
                "Khong truyen status se tra ve tat ca."));

        group.MapGet(GetBlogPostManagementDetail, "management/{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet blog de quan ly")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Danh cho Admin, Manager, Staff.",
                "Tra ve du content ke ca bai Draft hoac Archived."));

        group.MapPost(CreateBlogPost, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao blog moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateExample,
                "Danh cho Admin, Manager, Staff.",
                "status optional, mac dinh Draft. Khi tao moi chi hop le: Draft | Published.",
                "category bat buoc nhap. Gia tri hop le: Activity | Event | News.",
                "imageUrl la anh cover cua bai viet. Bai Published bat buoc co imageUrl.",
                "slug optional; neu khong truyen se tu sinh tu title va tu them hau to neu trung."));

        group.MapPut(UpdateBlogPost, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateExample,
                "Danh cho Admin, Manager, Staff.",
                "status hop le: Draft | Published | Archived.",
                "category hop le: Activity | Event | News.",
                "Bai Published bat buoc co imageUrl.",
                "slug optional; neu khong truyen se tu sinh lai tu title."));

        group.MapPost(PublishBlogPost, "{id:guid}/publish")
            .RequireAuthorization()
            .WithSummary("Xuat ban blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Danh cho Admin, Manager, Staff.",
                "Dat Status = Published va set PublishedAt neu chua co.",
                "Bai viet phai co imageUrl truoc khi publish."));

        group.MapDelete(ArchiveBlogPost, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Luu tru blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Soft delete: dat Status = Archived.",
                "Tra ve 204 khi thanh cong."));
    }

    private static async Task<IResult> GetPublishedBlogPosts(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPublishedBlogPostListQuery(), ct));

    private static async Task<IResult> GetPublishedBlogPostBySlug(ISender sender, string slug, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBlogPostBySlugQuery(slug), ct));

    private static async Task<IResult> GetBlogPostManagementList(
        ISender sender,
        string? status,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBlogPostManagementListQuery(status), ct));

    private static async Task<IResult> GetBlogPostManagementDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBlogPostManagementDetailQuery(id), ct));

    private static async Task<IResult> CreateBlogPost(
        ISender sender,
        CreateBlogPostCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateBlogPost(
        ISender sender,
        Guid id,
        UpdateBlogPostRequest req,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateBlogPostCommand(
            id,
            req.Title,
            req.Slug,
            req.Summary,
            req.Content,
            req.Status,
            req.ImageUrl,
            req.ImageAltText,
            req.Category), ct));

    private static async Task<IResult> PublishBlogPost(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new PublishBlogPostCommand(id), ct));

    private static async Task<IResult> ArchiveBlogPost(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new ArchiveBlogPostCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateBlogPostRequest(
        string Title,
        string? Slug,
        string? Summary,
        string Content,
        string Status,
        string? ImageUrl,
        string? ImageAltText,
        string Category);
}
