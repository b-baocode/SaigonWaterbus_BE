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
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateBlogPostCommandHandler(context, staffContext, new FixedTimeProvider(now));

        var first = await handler.Handle(
            new CreateBlogPostCommand(
                "Khám phá Sài Gòn bằng waterbus",
                null,
                "Tom tat",
                "Noi dung bai viet",
                "activity",
                "Published",
                "https://example.test/waterbus-cover.webp",
                "Tau waterbus tren song Sai Gon"),
            CancellationToken.None);

        var second = await handler.Handle(
            new CreateBlogPostCommand(
                "Khám phá Sài Gòn bằng waterbus",
                null,
                "Tom tat khac",
                "Noi dung bai viet khac",
                "News",
                "Published",
                "https://example.test/waterbus-cover-2.webp",
                "Tau waterbus tren song Sai Gon"),
            CancellationToken.None);

        first.Slug.ShouldBe("kham-pha-sai-gon-bang-waterbus");
        second.Slug.ShouldBe("kham-pha-sai-gon-bang-waterbus-2");
        first.Category.ShouldBe("Activity");
        second.Category.ShouldBe("News");
        first.ImageUrl.ShouldBe("https://example.test/waterbus-cover.webp");
        first.PublishedAt.ShouldBe(now);
        second.PublishedAt.ShouldBe(now);
        context.Set<BlogPost>().Count().ShouldBe(2);
    }

    [Test]
    public async Task CreatePublishedBlogPostRequiresImageUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            staffContext,
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
            .ShouldContain("Bai viet Published bat buoc co imageUrl.");
    }

    [Test]
    public async Task CreateBlogPostRequiresCategory()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var handler = new CreateBlogPostCommandHandler(
            context,
            staffContext,
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
            .ShouldContain("Category bat buoc nhap. Gia tri hop le: Activity | Event | News.");
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
    public async Task PublicListOnlyReturnsPublishedBlogPosts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var author = context.Users.Single(x => x.Id == staffContext.UserId!.Value);

        context.Set<BlogPost>().AddRange(
            BlogPost(author, "draft-post", "Draft", null),
            BlogPost(author, "published-post", "Published", new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero)),
            BlogPost(author, "archived-post", "Archived", new DateTimeOffset(2030, 1, 3, 1, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        var result = await new GetPublishedBlogPostListQueryHandler(context)
            .Handle(new GetPublishedBlogPostListQuery(), CancellationToken.None);

        result.Count.ShouldBe(1);
        result.Single().Slug.ShouldBe("published-post");
        result.Single().Category.ShouldBe("News");
        result.Single().ImageUrl.ShouldBe("https://example.test/waterbus-cover.webp");
    }

    private static BlogPost BlogPost(User author, string slug, string status, DateTimeOffset? publishedAt) =>
        new()
        {
            AuthorId = author.Id,
            Author = author,
            Title = slug,
            Slug = slug,
            Summary = "Tom tat",
            Category = "News",
            ImageUrl = status == "Published" ? "https://example.test/waterbus-cover.webp" : null,
            ImageAltText = status == "Published" ? "Tau waterbus tren song Sai Gon" : null,
            Content = "Noi dung",
            Status = status,
            PublishedAt = publishedAt,
            Created = publishedAt ?? new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)
        };

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
}
