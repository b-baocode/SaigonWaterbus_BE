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

    private const string UpdateImageExample =
        """
        {
          "imageUrl": "https://res.cloudinary.com/demo/image/upload/waterbus/blog-cover-fallback.webp",
          "imageAltText": "Tau waterbus tren song Sai Gon luc hoang hon"
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
            .DisableAntiforgery()
            .Accepts<CreateBlogPostFormRequest>("multipart/form-data")
            .Accepts<CreateBlogPostJsonRequest>("application/json")
            .WithSummary("Tao blog moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateExample,
                "Danh cho Admin, Manager, Staff.",
                "status optional, mac dinh Draft. Khi tao moi chi hop le: Draft | Published.",
                "category bat buoc nhap. Gia tri hop le: Activity | Event | News.",
                "imageUrl la anh cover cua bai viet. Bai Published bat buoc co imageUrl.",
                "Co the gui multipart/form-data voi field image de upload anh cover, giong boat/station.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "slug optional; neu khong truyen se tu sinh tu title va tu them hau to neu trung."));

        group.MapPut(UpdateBlogPost, "{id:guid}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateBlogPostFormRequest>("multipart/form-data")
            .Accepts<UpdateBlogPostRequest>("application/json")
            .WithSummary("Cap nhat blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateExample,
                "Danh cho Admin, Manager, Staff.",
                "status hop le: Draft | Published | Archived.",
                "category hop le: Activity | Event | News.",
                "Bai Published bat buoc co imageUrl.",
                "Co the gui multipart/form-data voi field image de upload/cap nhat anh cover, giong boat/station.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "slug optional; neu khong truyen se tu sinh lai tu title."));

        group.MapPatch(UpdateBlogPostImage, "{id:guid}/image")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<BlogPostImageFormRequest>("multipart/form-data")
            .Accepts<UpdateBlogPostImageRequest>("application/json")
            .WithSummary("Cap nhat anh cover blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateImageExample,
                "Danh cho Admin, Manager, Staff.",
                "Blog chi co 1 anh cover: imageUrl.",
                "Khuyen dung multipart/form-data voi field image va imageAltText.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Neu gui application/json thi imageUrl phai la absolute URL.",
                "Bai Published khong duoc xoa imageUrl."));

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
        HttpRequest request,
        CancellationToken ct)
    {
        var command = request.HasFormContentType
            ? await CreateBlogPostCommandFromFormAsync(request, ct)
            : await CreateBlogPostCommandFromJsonAsync(request, ct);
        if (command is null)
        {
            return Results.BadRequest(new { message = "Gửi application/json hoặc multipart/form-data." });
        }

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            command.ImageFile?.Content.Dispose();
        }
    }

    private static async Task<IResult> UpdateBlogPost(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var command = request.HasFormContentType
            ? await UpdateBlogPostCommandFromFormAsync(id, request, ct)
            : await UpdateBlogPostCommandFromJsonAsync(id, request, ct);
        if (command is null)
        {
            return Results.BadRequest(new { message = "Gửi application/json hoặc multipart/form-data." });
        }

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            command.ImageFile?.Content.Dispose();
        }
    }

    private static async Task<IResult> UpdateBlogPostImage(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var command = request.HasFormContentType
            ? await UpdateBlogPostImageCommandFromFormAsync(id, request, ct)
            : await UpdateBlogPostImageCommandFromJsonAsync(id, request, ct);
        if (command is null)
        {
            return Results.BadRequest(new { message = "Gửi multipart/form-data với field image hoặc application/json với imageUrl." });
        }

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            command.ImageFile?.Content.Dispose();
        }
    }

    private static async Task<IResult> PublishBlogPost(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new PublishBlogPostCommand(id), ct));

    private static async Task<IResult> ArchiveBlogPost(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new ArchiveBlogPostCommand(id), ct);
        return Results.NoContent();
    }

    private sealed record CreateBlogPostJsonRequest(
        string Title,
        string? Slug,
        string? Summary,
        string Content,
        string Category,
        string? Status,
        string? ImageUrl = null,
        string? ImageAltText = null);

    private sealed record CreateBlogPostFormRequest(
        string Title,
        string? Slug,
        string? Summary,
        string Content,
        string Category,
        string? Status,
        string? ImageUrl = null,
        string? ImageAltText = null,
        IFormFile? Image = null);

    public sealed record UpdateBlogPostRequest(
        string Title,
        string? Slug,
        string? Summary,
        string Content,
        string Status,
        string? ImageUrl,
        string? ImageAltText,
        string Category);

    private sealed record UpdateBlogPostFormRequest(
        string Title,
        string? Slug,
        string? Summary,
        string Content,
        string Status,
        string? ImageUrl,
        string? ImageAltText,
        string Category,
        IFormFile? Image = null);

    public sealed record UpdateBlogPostImageRequest(
        string? ImageUrl,
        string? ImageAltText = null);

    private sealed record BlogPostImageFormRequest(
        IFormFile Image,
        string? ImageAltText = null);

    private static async Task<CreateBlogPostCommand?> CreateBlogPostCommandFromJsonAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        var body = await request.ReadFromJsonAsync<CreateBlogPostJsonRequest>(cancellationToken: ct);
        return body is null
            ? null
            : new CreateBlogPostCommand(
                body.Title,
                body.Slug,
                body.Summary,
                body.Content,
                body.Category,
                body.Status,
                body.ImageUrl,
                body.ImageAltText);
    }

    private static async Task<CreateBlogPostCommand> CreateBlogPostCommandFromFormAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        return new CreateBlogPostCommand(
            GetFormValue(form, "title") ?? string.Empty,
            GetFormValue(form, "slug"),
            GetFormValue(form, "summary"),
            GetFormValue(form, "content") ?? string.Empty,
            GetFormValue(form, "category") ?? string.Empty,
            GetFormValue(form, "status"),
            GetFormValue(form, "imageUrl"),
            GetFormValue(form, "imageAltText"),
            await CreateImageFileFromFormAsync(form, ct));
    }

    private static async Task<UpdateBlogPostCommand?> UpdateBlogPostCommandFromJsonAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var body = await request.ReadFromJsonAsync<UpdateBlogPostRequest>(cancellationToken: ct);
        return body is null
            ? null
            : new UpdateBlogPostCommand(
                id,
                body.Title,
                body.Slug,
                body.Summary,
                body.Content,
                body.Status,
                body.ImageUrl,
                body.ImageAltText,
                body.Category);
    }

    private static async Task<UpdateBlogPostCommand> UpdateBlogPostCommandFromFormAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        return new UpdateBlogPostCommand(
            id,
            GetFormValue(form, "title") ?? string.Empty,
            GetFormValue(form, "slug"),
            GetFormValue(form, "summary"),
            GetFormValue(form, "content") ?? string.Empty,
            GetFormValue(form, "status") ?? string.Empty,
            GetFormValue(form, "imageUrl"),
            GetFormValue(form, "imageAltText"),
            GetFormValue(form, "category") ?? string.Empty,
            await CreateImageFileFromFormAsync(form, ct));
    }

    private static async Task<UpdateBlogPostImageCommand?> UpdateBlogPostImageCommandFromJsonAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var body = await request.ReadFromJsonAsync<UpdateBlogPostImageRequest>(cancellationToken: ct);
        return body is null
            ? null
            : new UpdateBlogPostImageCommand(id, body.ImageUrl, body.ImageAltText);
    }

    private static async Task<UpdateBlogPostImageCommand?> UpdateBlogPostImageCommandFromFormAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var imageFile = await CreateImageFileFromFormAsync(form, ct);
        if (imageFile is null)
        {
            return null;
        }

        return new UpdateBlogPostImageCommand(
            id,
            null,
            GetFormValue(form, "imageAltText"),
            imageFile);
    }

    private static async Task<BlogPostImageFileRequest?> CreateImageFileFromFormAsync(
        IFormCollection form,
        CancellationToken ct)
    {
        var file = form.Files.FirstOrDefault(file =>
            string.Equals(file.Name, "image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return null;
        }

        var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;

        return new BlogPostImageFileRequest(
            file.FileName,
            file.ContentType,
            file.Length,
            content);
    }

    private static string? GetFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
