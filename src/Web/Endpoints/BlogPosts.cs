using System.Text.Json;
using SaigonWaterbus.Application.BlogPosts;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class BlogPosts : IEndpointGroup
{
    public static string RoutePrefix => "/api/blog-posts";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string UploadOnlyImageMessage = "Không hỗ trợ gắn link ảnh blog; vui lòng upload file ảnh.";

    private const string CreateExample =
        """
        {
          "title": "Kham pha Sai Gon bang waterbus",
          "summary": "Nhung diem nen trai nghiem tren tuyen waterbus Sai Gon.",
          "category": "Activity",
          "imageAltText": "Tau waterbus tren song Sai Gon",
          "content": "Noi dung bai viet dang van ban thuong, khong can HTML.",
          "status": "Draft"
        }
        """;

    private const string UpdateExample =
        """
        {
          "title": "Kham pha Sai Gon bang waterbus",
          "summary": "Nhung diem nen trai nghiem tren tuyen waterbus Sai Gon.",
          "category": "News",
          "imageAltText": "Tau waterbus tren song Sai Gon",
          "content": "Noi dung bai viet da cap nhat bang text thuong.",
          "status": "Published"
        }
        """;

    private const string UpdateImageExample =
        """
        multipart/form-data
        image=<file>
        images=<file>
        imageAltText=Tau waterbus tren song Sai Gon luc hoang hon
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
                "Chi danh cho Admin.",
                "Query params: status (optional): Draft | Published | Archived.",
                "Khong truyen status se tra ve tat ca."));

        group.MapGet(GetBlogPostManagementDetail, "management/{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet blog de quan ly")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi danh cho Admin.",
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
                "Chi danh cho Admin.",
                "Khong co luong duyet/tac gia tren FE; Admin tao xong tu chon Draft de an hoac Published de hien.",
                "status optional, mac dinh Draft. Khi tao moi chi hop le: Draft | Published.",
                "category bat buoc nhap. Gia tri hop le: Activity | Event | News.",
                "slug khong can gui; BE tu sinh tu title va tu them hau to neu trung.",
                "content nhap plain text; BE tra them contentText de admin doc va contentHtml da convert an toan cho FE render.",
                "Anh blog chi nhan upload file bang multipart/form-data field image/images/files, khong ho tro gan link imageUrl/imageUrls.",
                "Response van tra imageUrl/imageUrls vi file upload se duoc luu thanh URL de FE hien thi.",
                "Bai Published bat buoc co it nhat 1 anh.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "FE khong can hien input slug."));

        group.MapPut(UpdateBlogPost, "{id:guid}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateBlogPostFormRequest>("multipart/form-data")
            .Accepts<UpdateBlogPostRequest>("application/json")
            .WithSummary("Cap nhat blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateExample,
                "Chi danh cho Admin.",
                "Khong co luong duyet/tac gia tren FE; Admin doi status de an/hien bai.",
                "status hop le: Draft | Published | Archived.",
                "category hop le: Activity | Event | News.",
                "slug khong can gui; neu bo trong BE tu sinh lai tu title.",
                "content nhap plain text; BE tra them contentText va contentHtml.",
                "Bai Published bat buoc co it nhat 1 anh.",
                "Neu gui file image/images/files thi BE thay toan bo danh sach anh bang file moi upload.",
                "Neu khong gui file anh thi BE giu nguyen danh sach anh hien tai.",
                "Khong ho tro gan link imageUrl/imageUrls trong request.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "FE khong can hien input slug."));

        group.MapPatch(UpdateBlogPostImage, "{id:guid}/image")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<BlogPostImageFormRequest>("multipart/form-data")
            .WithSummary("Cap nhat anh cover blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateImageExample,
                "Chi danh cho Admin.",
                "Chi nhan multipart/form-data voi field image/images/files va imageAltText de upload 1 hoac nhieu anh.",
                "Endpoint nay thay toan bo danh sach anh hien tai bang file moi upload.",
                "Khong ho tro application/json, imageUrl hoac imageUrls.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Bai Published khong duoc xoa het anh."));

        group.MapPost(PublishBlogPost, "{id:guid}/publish")
            .RequireAuthorization()
            .WithSummary("Xuat ban blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi danh cho Admin.",
                "Dat Status = Published va set PublishedAt neu chua co.",
                "Bai viet phai co it nhat 1 anh truoc khi publish."));

        group.MapDelete(ArchiveBlogPost, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Luu tru blog")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi danh cho Admin.",
                "Soft delete/an bai: dat Status = Archived.",
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
            DisposeImageContents(command.ImageFile, command.ImageFiles);
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
            DisposeImageContents(command.ImageFile, command.ImageFiles);
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
            : null;
        if (command is null)
        {
            return Results.BadRequest(new { message = "Gửi multipart/form-data với field image/images/files. Không hỗ trợ gắn link imageUrl/imageUrls." });
        }

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            DisposeImageContents(command.ImageFile, command.ImageFiles);
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
        string? Summary,
        string Content,
        string Category,
        string? Status,
        string? ImageAltText = null);

    private sealed record CreateBlogPostFormRequest(
        string Title,
        string? Summary,
        string Content,
        string Category,
        string? Status,
        string? ImageAltText = null,
        IFormFile? Image = null,
        IFormFileCollection? Images = null);

    public sealed record UpdateBlogPostRequest(
        string Title,
        string? Summary,
        string Content,
        string Status,
        string? ImageAltText,
        string Category);

    private sealed record UpdateBlogPostFormRequest(
        string Title,
        string? Summary,
        string Content,
        string Status,
        string? ImageAltText,
        string Category,
        IFormFile? Image = null,
        IFormFileCollection? Images = null);

    private sealed record BlogPostImageFormRequest(
        IFormFile? Image = null,
        IFormFileCollection? Images = null,
        string? ImageAltText = null);

    private static async Task<CreateBlogPostCommand?> CreateBlogPostCommandFromJsonAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        var body = document.RootElement.Deserialize<CreateBlogPostJsonRequest>(JsonOptions);
        return body is null
            ? null
            : new CreateBlogPostCommand(
                Title: body.Title,
                Slug: null,
                Summary: body.Summary,
                Content: body.Content,
                Category: body.Category,
                Status: body.Status,
                ImageUrl: CreateRejectedImageUrlMarker(document.RootElement),
                ImageAltText: body.ImageAltText,
                ImageUrls: CreateRejectedImageUrlsMarker(document.RootElement));
    }

    private static async Task<CreateBlogPostCommand> CreateBlogPostCommandFromFormAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var imageUrls = GetFormValues(form, "imageUrls", "imageUrls[]");
        var imageFiles = await CreateImageFilesFromFormAsync(form, ct);

        return new CreateBlogPostCommand(
            Title: GetFormValue(form, "title") ?? string.Empty,
            Slug: null,
            Summary: GetFormValue(form, "summary"),
            Content: GetFormValue(form, "content") ?? string.Empty,
            Category: GetFormValue(form, "category") ?? string.Empty,
            Status: GetFormValue(form, "status"),
            ImageUrl: GetFormValue(form, "imageUrl"),
            ImageAltText: GetFormValue(form, "imageAltText"),
            ImageUrls: imageUrls,
            ImageFiles: imageFiles);
    }

    private static async Task<UpdateBlogPostCommand?> UpdateBlogPostCommandFromJsonAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        var body = document.RootElement.Deserialize<UpdateBlogPostRequest>(JsonOptions);
        return body is null
            ? null
            : new UpdateBlogPostCommand(
                BlogPostId: id,
                Title: body.Title,
                Slug: null,
                Summary: body.Summary,
                Content: body.Content,
                Status: body.Status,
                ImageUrl: CreateRejectedImageUrlMarker(document.RootElement),
                ImageAltText: body.ImageAltText,
                Category: body.Category,
                ImageUrls: CreateRejectedImageUrlsMarker(document.RootElement));
    }

    private static async Task<UpdateBlogPostCommand> UpdateBlogPostCommandFromFormAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var imageUrls = GetFormValues(form, "imageUrls", "imageUrls[]");
        var imageFiles = await CreateImageFilesFromFormAsync(form, ct);

        return new UpdateBlogPostCommand(
            BlogPostId: id,
            Title: GetFormValue(form, "title") ?? string.Empty,
            Slug: null,
            Summary: GetFormValue(form, "summary"),
            Content: GetFormValue(form, "content") ?? string.Empty,
            Status: GetFormValue(form, "status") ?? string.Empty,
            ImageUrl: GetFormValue(form, "imageUrl"),
            ImageAltText: GetFormValue(form, "imageAltText"),
            Category: GetFormValue(form, "category") ?? string.Empty,
            ImageUrls: imageUrls,
            ImageFiles: imageFiles);
    }

    private static async Task<UpdateBlogPostImageCommand?> UpdateBlogPostImageCommandFromFormAsync(
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var imageUrl = GetFormValue(form, "imageUrl");
        var imageUrls = GetFormValues(form, "imageUrls", "imageUrls[]");
        var imageFiles = await CreateImageFilesFromFormAsync(form, ct);
        if (string.IsNullOrWhiteSpace(imageUrl) && imageUrls.Count == 0 && imageFiles.Count == 0)
        {
            return null;
        }

        return new UpdateBlogPostImageCommand(
            BlogPostId: id,
            ImageUrl: imageUrl,
            ImageAltText: GetFormValue(form, "imageAltText"),
            ImageUrls: imageUrls,
            ImageFiles: imageFiles);
    }

    private static string? CreateRejectedImageUrlMarker(JsonElement root) =>
        ContainsManualImageInput(root, "imageUrl") ? UploadOnlyImageMessage : null;

    private static IReadOnlyCollection<string>? CreateRejectedImageUrlsMarker(JsonElement root) =>
        ContainsManualImageInput(root, "imageUrls") ? [UploadOnlyImageMessage] : null;

    private static bool ContainsManualImageInput(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryGetPropertyIgnoreCase(root, propertyName, out var value))
        {
            return false;
        }

        return ContainsNonBlankJsonValue(value);
    }

    private static bool ContainsNonBlankJsonValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.EnumerateArray().Any(ContainsNonBlankJsonValue),
            _ => true
        };

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static async Task<IReadOnlyCollection<BlogPostImageFileRequest>> CreateImageFilesFromFormAsync(
        IFormCollection form,
        CancellationToken ct)
    {
        var files = form.Files
            .Where(file =>
                string.Equals(file.Name, "image", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Name, "images", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Name, "files", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            return [];
        }

        var imageFiles = new List<BlogPostImageFileRequest>(files.Count);
        foreach (var file in files)
        {
            var content = new MemoryStream();
            await file.CopyToAsync(content, ct);
            content.Position = 0;

            imageFiles.Add(new BlogPostImageFileRequest(
                file.FileName,
                file.ContentType,
                file.Length,
                content));
        }

        return imageFiles;
    }

    private static string? GetFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyCollection<string> GetFormValues(IFormCollection form, params string[] names)
    {
        var values = new List<string>();
        foreach (var name in names)
        {
            foreach (var rawValue in form[name])
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                foreach (var value in rawValue.Split(
                    [',', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        values.Add(value);
                    }
                }
            }
        }

        return values;
    }

    private static void DisposeImageContents(
        BlogPostImageFileRequest? imageFile,
        IReadOnlyCollection<BlogPostImageFileRequest>? imageFiles)
    {
        imageFile?.Content.Dispose();
        if (imageFiles is null)
        {
            return;
        }

        foreach (var file in imageFiles)
        {
            if (!ReferenceEquals(file, imageFile))
            {
                file.Content.Dispose();
            }
        }
    }
}
