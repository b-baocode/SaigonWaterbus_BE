using NUnit.Framework;
using SaigonWaterbus.Application.BlogPosts;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BlogPosts;

public class BlogPostCommandTests
{
    [Test]
    public async Task CreatePublishedBlogPostGeneratesUniqueSlug()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(now),
            new TestBlogImageStorageService());

        var first = await handler.Handle(
            new CreateBlogPostCommand(
                Title: "Khám phá Sài Gòn bằng waterbus",
                Slug: null,
                Summary: "Tom tat",
                Content: "Noi dung bai viet",
                Category: "event",
                Status: "Published",
                ImageAltText: "Tau waterbus tren song Sai Gon",
                ImageFile: ImageFile("waterbus-cover.webp")),
            CancellationToken.None);

        var second = await handler.Handle(
            new CreateBlogPostCommand(
                Title: "Khám phá Sài Gòn bằng waterbus",
                Slug: null,
                Summary: "Tom tat khac",
                Content: "Noi dung bai viet khac",
                Category: "News",
                Status: "Published",
                ImageAltText: "Tau waterbus tren song Sai Gon",
                ImageFile: ImageFile("waterbus-cover-2.webp")),
            CancellationToken.None);

        first.Slug.ShouldBe("kham-pha-sai-gon-bang-waterbus");
        second.Slug.ShouldBe("kham-pha-sai-gon-bang-waterbus-2");
        first.Category.ShouldBe("Event");
        second.Category.ShouldBe("News");
        first.ImageUrl.ShouldBe($"https://example.test/blog-posts/{first.BlogPostId}/waterbus-cover.webp");
        first.PublishedAt.ShouldBe(now);
        second.PublishedAt.ShouldBe(now);
        context.Set<BlogPost>().Count().ShouldBe(2);
    }

    [Test]
    public async Task CreateBlogPostWithBlankSlugReturnsReadableContentAndMultipleImages()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)),
            new TestBlogImageStorageService());

        var result = await handler.Handle(
            new CreateBlogPostCommand(
                Title: "Bài viết nhiều ảnh",
                Slug: "",
                Summary: "Tom tat",
                Content: "Dong 1\nDong 2\n\nDong 3",
                Category: "News",
                Status: "Draft",
                ImageFiles:
                [
                    ImageFile("blog-1.webp"),
                    ImageFile("blog-2.webp")
                ]),
            CancellationToken.None);

        result.Slug.ShouldBe("bai-viet-nhieu-anh");
        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{result.BlogPostId}/blog-1.webp");
        result.ImageUrls.ShouldBe([
            $"https://example.test/blog-posts/{result.BlogPostId}/blog-1.webp",
            $"https://example.test/blog-posts/{result.BlogPostId}/blog-2.webp"
        ]);
        result.ContentText.ShouldBe("Dong 1\nDong 2\n\nDong 3");
        result.ContentHtml.ShouldBe("<p>Dong 1<br>Dong 2</p><p>Dong 3</p>");
    }

    [Test]
    public async Task CreatePublishedBlogPostRequiresUploadedImage()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateBlogPostCommand(
                    "Bai viet chua co anh",
                    null,
                    "Tom tat",
                    "Noi dung bai viet",
                    "News",
                    "Published"),
                CancellationToken.None));

        exception.Errors["imageUrl"]
            .ShouldContain("Bai viet Published bat buoc co it nhat 1 anh.");
    }

    [Test]
    public async Task CreateBlogPostRejectsManualImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateBlogPostCommand(
                    "Bai viet gan link anh",
                    null,
                    "Tom tat",
                    "Noi dung bai viet",
                    "News",
                    "Draft",
                    ImageUrl: "https://example.test/manual.webp"),
                CancellationToken.None));

        exception.Errors["imageUrl"]
            .ShouldContain("Không hỗ trợ gắn link ảnh blog; vui lòng upload file ảnh.");
    }

    [Test]
    public async Task CreateBlogPostCanUploadImageFile()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(now),
            new TestBlogImageStorageService());

        var result = await handler.Handle(
            new CreateBlogPostCommand(
                "Blog co file anh",
                null,
                "Tom tat",
                "Noi dung bai viet",
                "News",
                "Published",
                ImageAltText: "Uploaded cover",
                ImageFile: ImageFile("cover.webp")),
            CancellationToken.None);

        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{result.BlogPostId}/cover.webp");
        result.ImageAltText.ShouldBe("Uploaded cover");
        result.PublishedAt.ShouldBe(now);
    }

    [Test]
    public async Task CreateBlogPostRequiresCategory()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateBlogPostCommand(
                    "Bai viet thieu category",
                    null,
                    "Tom tat",
                    "Noi dung bai viet",
                    null!,
                    "Draft"),
                CancellationToken.None));

        exception.Errors["category"]
            .ShouldContain("Category bat buoc nhap. Gia tri hop le: News | Event.");
    }

    [Test]
    public async Task CustomerCannotCreateBlogPost()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            customerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new CreateBlogPostCommand(
                    "Customer post",
                    null,
                    null,
                    "Noi dung bai viet",
                    "News",
                    "Draft"),
                CancellationToken.None));
    }

    [Test]
    public async Task StaffCannotCreateBlogPost()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new CreateBlogPostCommand(
                    "Staff post",
                    null,
                    null,
                    "Noi dung bai viet",
                    "News",
                    "Draft"),
                CancellationToken.None));
    }

    [Test]
    public async Task AdminCanUpdateBlogPostImage()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostImageCommandHandler(
            context,
            adminContext,
            new TestBlogImageStorageService());
        var result = await handler.Handle(
            new UpdateBlogPostImageCommand(
                BlogPostId: post.Id,
                ImageUrl: null,
                ImageAltText: "New cover",
                ImageFile: ImageFile("new-cover.webp")),
            CancellationToken.None);

        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/new-cover.webp");
        result.ImageAltText.ShouldBe("New cover");

        var savedPost = context.Set<BlogPost>().Single(x => x.Id == post.Id);
        savedPost.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/new-cover.webp");
        savedPost.ImageAltText.ShouldBe("New cover");
    }

    [Test]
    public async Task StaffCannotUpdateBlogPostImage()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostImageCommandHandler(
            context,
            staffContext,
            new TestBlogImageStorageService());
        await Should.ThrowAsync<ForbiddenAccessException>(() => handler.Handle(
            new UpdateBlogPostImageCommand(
                BlogPostId: post.Id,
                ImageUrl: null,
                ImageAltText: "New cover",
                ImageFile: ImageFile("new-cover.webp")),
            CancellationToken.None));
    }

    [Test]
    public async Task AdminCanUploadMultipleBlogPostImages()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostImageCommandHandler(
            context,
            adminContext,
            new TestBlogImageStorageService());
        var result = await handler.Handle(
            new UpdateBlogPostImageCommand(
                BlogPostId: post.Id,
                ImageUrl: null,
                ImageAltText: "Gallery",
                ImageFiles:
                [
                    ImageFile("gallery-1.webp"),
                    ImageFile("gallery-2.webp")
                ]),
            CancellationToken.None);

        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/gallery-1.webp");
        result.ImageUrls.ShouldBe([
            $"https://example.test/blog-posts/{post.Id}/gallery-1.webp",
            $"https://example.test/blog-posts/{post.Id}/gallery-2.webp"
        ]);

        var savedPost = context.Set<BlogPost>().Single(x => x.Id == post.Id);
        savedPost.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/gallery-1.webp");
        savedPost.ImageUrls.ShouldBe([
            $"https://example.test/blog-posts/{post.Id}/gallery-1.webp",
            $"https://example.test/blog-posts/{post.Id}/gallery-2.webp"
        ]);
    }

    [Test]
    public async Task AdminCanUploadBlogPostImageFile()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostImageCommandHandler(
            context,
            adminContext,
            new TestBlogImageStorageService());
        var result = await handler.Handle(
            new UpdateBlogPostImageCommand(
                post.Id,
                null,
                "Uploaded cover",
                ImageFile("cover.webp")),
            CancellationToken.None);

        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/cover.webp");
        result.ImageAltText.ShouldBe("Uploaded cover");

        var savedPost = context.Set<BlogPost>().Single(x => x.Id == post.Id);
        savedPost.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/cover.webp");
    }

    [Test]
    public async Task UpdateBlogPostCanUploadImageFile()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostCommandHandler(
            context,
            adminContext,
            TimeProvider.System,
            new TestBlogImageStorageService());
        var result = await handler.Handle(
            new UpdateBlogPostCommand(
                post.Id,
                "Draft post updated",
                "draft-post-updated",
                "Tom tat moi",
                "Noi dung moi",
                "Draft",
                null,
                "Updated cover",
                "Event",
                ImageFile("updated-cover.webp")),
            CancellationToken.None);

        result.ImageUrl.ShouldBe($"https://example.test/blog-posts/{post.Id}/updated-cover.webp");
        result.ImageAltText.ShouldBe("Updated cover");
        result.Category.ShouldBe("Event");
    }

    [Test]
    public async Task UpdateBlogPostWithoutImageKeepsExistingImage()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(
            author,
            "published-post",
            "Published",
            new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero));
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostCommandHandler(
            context,
            adminContext,
            TimeProvider.System,
            new TestBlogImageStorageService());
        var result = await handler.Handle(
            new UpdateBlogPostCommand(
                post.Id,
                "Published post updated",
                null,
                "Tom tat moi",
                "Noi dung moi",
                "Published",
                null,
                "Tau waterbus tren song Sai Gon",
                "News"),
            CancellationToken.None);

        result.ImageUrl.ShouldBe("https://example.test/waterbus-cover.webp");
        result.ImageUrls.ShouldBe(["https://example.test/waterbus-cover.webp"]);
    }

    [Test]
    public async Task UpdateBlogPostStatusUsesRequestedDraftOrPublishedStatus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        post.ImageUrl = "https://example.test/waterbus-cover.webp";
        post.ImageUrls = ["https://example.test/waterbus-cover.webp"];
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var publishedAt = new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero);
        var handler = new UpdateBlogPostStatusCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(publishedAt));

        var published = await handler.Handle(
            new UpdateBlogPostStatusCommand(post.Id, "Published"),
            CancellationToken.None);

        published.Status.ShouldBe("Published");
        published.PublishedAt.ShouldBe(publishedAt);

        var draft = await handler.Handle(
            new UpdateBlogPostStatusCommand(post.Id, "Draft"),
            CancellationToken.None);

        draft.Status.ShouldBe("Draft");
        draft.PublishedAt.ShouldBeNull();
    }

    [Test]
    public async Task DeleteBlogPostRemovesPostInsteadOfArchiving()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new DeleteBlogPostCommandHandler(context, adminContext);

        await handler.Handle(new DeleteBlogPostCommand(post.Id), CancellationToken.None);

        context.Set<BlogPost>().Any(x => x.Id == post.Id).ShouldBeFalse();
    }

    [Test]
    public async Task UpdateBlogPostImageRequiresUploadedFile()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var post = BlogPost(author, "draft-post", "Draft", null);
        context.Set<BlogPost>().Add(post);
        await context.SaveChangesAsync();

        var handler = new UpdateBlogPostImageCommandHandler(context, adminContext);
        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateBlogPostImageCommand(post.Id, null, null),
                CancellationToken.None));

        exception.Errors["imageFile"]
            .ShouldContain("Vui lòng upload ít nhất 1 file ảnh blog.");
    }

    [Test]
    public async Task PublicListOnlyReturnsPublishedBlogPosts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);

        context.Set<BlogPost>().AddRange(
            BlogPost(author, "draft-post", "Draft", null),
            BlogPost(author, "published-post", "Published", new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero)),
            BlogPost(author, "activity-post", "Published", new DateTimeOffset(2030, 1, 4, 1, 0, 0, TimeSpan.Zero), "Activity"),
            BlogPost(author, "archived-post", "Archived", new DateTimeOffset(2030, 1, 3, 1, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        var result = await new GetPublishedBlogPostListQueryHandler(context)
            .Handle(new GetPublishedBlogPostListQuery(), CancellationToken.None);

        result.Count.ShouldBe(1);
        result.Single().Slug.ShouldBe("published-post");
        result.Single().Category.ShouldBe("News");
        result.Single().ImageUrl.ShouldBe("https://example.test/waterbus-cover.webp");
    }

    [Test]
    public async Task ManagementListOnlyReturnsDraftAndPublishedBlogPosts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var author = context.Users.Single(x => x.Id == adminContext.UserId!.Value);

        context.Set<BlogPost>().AddRange(
            BlogPost(author, "draft-post", "Draft", null),
            BlogPost(author, "published-post", "Published", new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero)),
            BlogPost(author, "activity-post", "Published", new DateTimeOffset(2030, 1, 4, 1, 0, 0, TimeSpan.Zero), "Activity"),
            BlogPost(author, "archived-post", "Archived", new DateTimeOffset(2030, 1, 3, 1, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        var result = await new GetBlogPostManagementListQueryHandler(context, adminContext)
            .Handle(new GetBlogPostManagementListQuery(null), CancellationToken.None);

        result.Select(x => x.Slug).ShouldBe(["published-post", "draft-post"]);
    }

    [Test]
    public void BlogPostStatusValidatorsRejectArchived()
    {
        new UpdateBlogPostCommandValidator()
            .Validate(new UpdateBlogPostCommand(
                Guid.NewGuid(),
                "Title",
                null,
                null,
                "Content",
                "Archived",
                null,
                null,
                "News"))
            .IsValid
            .ShouldBeFalse();

        new UpdateBlogPostStatusCommandValidator()
            .Validate(new UpdateBlogPostStatusCommand(Guid.NewGuid(), "Archived"))
            .IsValid
            .ShouldBeFalse();

        new GetBlogPostManagementListQueryValidator()
            .Validate(new GetBlogPostManagementListQuery("Archived"))
            .IsValid
            .ShouldBeFalse();
    }

    [Test]
    public void BlogPostCategoryValidatorsRejectActivity()
    {
        new CreateBlogPostCommandValidator()
            .Validate(new CreateBlogPostCommand(
                "Title",
                null,
                null,
                "Content",
                "Activity",
                "Draft"))
            .IsValid
            .ShouldBeFalse();

        new UpdateBlogPostCommandValidator()
            .Validate(new UpdateBlogPostCommand(
                Guid.NewGuid(),
                "Title",
                null,
                null,
                "Content",
                "Draft",
                null,
                null,
                "Activity"))
            .IsValid
            .ShouldBeFalse();
    }

    private static BlogPost BlogPost(
        User author,
        string slug,
        string status,
        DateTimeOffset? publishedAt,
        string category = "News") =>
        new()
        {
            AuthorId = author.Id,
            Author = author,
            Title = slug,
            Slug = slug,
            Summary = "Tom tat",
            Category = category,
            ImageUrl = status == "Published" ? "https://example.test/waterbus-cover.webp" : null,
            ImageAltText = status == "Published" ? "Tau waterbus tren song Sai Gon" : null,
            Content = "Noi dung",
            Status = status,
            PublishedAt = publishedAt,
            Created = publishedAt ?? new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)
        };

    private static BlogPostImageFileRequest ImageFile(string fileName) =>
        new(
            fileName,
            "image/webp",
            16,
            new MemoryStream(new byte[16]));

    private static async Task<IUserContext> SeedCustomerAsync(Infrastructure.Data.ApplicationDbContext context)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Blog customer",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return new TestBlogUserContext(user.Id);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestBlogUserContext(Guid userId) : IUserContext
    {
        public Guid? UserId { get; } = userId;

        public bool IsAuthenticated => true;
    }

    private sealed class TestBlogImageStorageService : IBlogImageStorageService
    {
        public long MaxImageBytes => 5 * 1024 * 1024;

        public IReadOnlyCollection<string> AllowedImageContentTypes { get; } =
            ["image/jpeg", "image/png", "image/webp"];

        public Task<StoredBlogImage> UploadImageAsync(
            BlogImageUpload upload,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StoredBlogImage(
                $"https://example.test/blog-posts/{upload.BlogPostId}/{upload.FileName}",
                $"{upload.BlogPostId}/{upload.FileName}"));
        }
    }
}
