using System.Globalization;
using System.Text;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.BlogPosts;

internal static class BlogPostSupport
{
    public const string DraftStatus = "Draft";
    public const string PublishedStatus = "Published";
    public const string ArchivedStatus = "Archived";

    private const int MaxSlugLength = 220;

    public static bool IsValidStatus(string? status)
    {
        var normalized = status?.Trim();
        return string.Equals(normalized, DraftStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, PublishedStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, ArchivedStatus, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeStatus(string? status, string propertyName)
    {
        var normalized = status?.Trim();
        if (string.Equals(normalized, DraftStatus, StringComparison.OrdinalIgnoreCase))
        {
            return DraftStatus;
        }

        if (string.Equals(normalized, PublishedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PublishedStatus;
        }

        if (string.Equals(normalized, ArchivedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return ArchivedStatus;
        }

        throw CreateValidationException(propertyName, "Status hop le: Draft | Published | Archived.");
    }

    public static async Task<User> EnsureCurrentUserCanManageBlogPostsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static string? NormalizeOptionalText(string? value, string propertyName, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (maxLength.HasValue && normalized.Length > maxLength.Value)
        {
            throw CreateValidationException(propertyName, $"{propertyName} toi da {maxLength.Value} ky tu.");
        }

        return normalized;
    }

    public static string NormalizeRequiredText(string? value, string propertyName, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateValidationException(propertyName, $"{propertyName} khong duoc de trong.");
        }

        var normalized = value.Trim();
        if (maxLength.HasValue && normalized.Length > maxLength.Value)
        {
            throw CreateValidationException(propertyName, $"{propertyName} toi da {maxLength.Value} ky tu.");
        }

        return normalized;
    }

    public static string? NormalizeImageUrl(string? imageUrl, string propertyName)
    {
        var normalized = NormalizeOptionalText(imageUrl, propertyName, 2048);
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw CreateValidationException(propertyName, "ImageUrl phai la absolute URL.");
        }

        return normalized;
    }

    public static void EnsurePublishedPostHasImage(BlogPost post, string propertyName)
    {
        if (post.Status == PublishedStatus && string.IsNullOrWhiteSpace(post.ImageUrl))
        {
            throw CreateValidationException(propertyName, "Bai viet Published bat buoc co imageUrl.");
        }
    }

    public static async Task<string> GenerateUniqueSlugAsync(
        IApplicationDbContext context,
        string slugSource,
        Guid? excludedBlogPostId,
        CancellationToken cancellationToken)
    {
        var baseSlug = NormalizeSlug(slugSource);
        var slug = baseSlug;
        var suffix = 2;

        while (await SlugExistsAsync(context, slug, excludedBlogPostId, cancellationToken))
        {
            var suffixText = $"-{suffix.ToString(CultureInfo.InvariantCulture)}";
            var maxBaseLength = MaxSlugLength - suffixText.Length;
            var truncatedBaseSlug = baseSlug.Length <= maxBaseLength
                ? baseSlug
                : baseSlug[..maxBaseLength].Trim('-');

            if (truncatedBaseSlug.Length == 0)
            {
                throw CreateValidationException("Slug", "Slug khong hop le.");
            }

            slug = $"{truncatedBaseSlug}{suffixText}";
            suffix++;
        }

        return slug;
    }

    public static string NormalizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateValidationException("Slug", "Slug khong duoc de trong.");
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower == 'đ')
            {
                lower = 'd';
            }

            if (char.IsLetterOrDigit(lower))
            {
                builder.Append(lower);
                previousWasSeparator = false;
                continue;
            }

            if (builder.Length > 0 && !previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            throw CreateValidationException("Slug", "Slug khong hop le.");
        }

        return slug.Length <= MaxSlugLength
            ? slug
            : slug[..MaxSlugLength].Trim('-');
    }

    public static BlogPostDto ToDto(BlogPost post) =>
        new(
            post.Id,
            post.AuthorId,
            post.Author.FullName,
            post.Title,
            post.Slug,
            post.Summary,
            post.ImageUrl,
            post.ImageAltText,
            post.Content,
            post.Status,
            post.PublishedAt,
            post.Created);

    private static async Task<bool> SlugExistsAsync(
        IApplicationDbContext context,
        string slug,
        Guid? excludedBlogPostId,
        CancellationToken cancellationToken)
    {
        var query = context.Set<BlogPost>().Where(x => x.Slug == slug);
        if (excludedBlogPostId.HasValue)
        {
            query = query.Where(x => x.Id != excludedBlogPostId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    private static SaigonWaterbus.Application.Common.Exceptions.ValidationException CreateValidationException(
        string propertyName,
        string errorMessage) =>
        new([new ValidationFailure(propertyName, errorMessage)]);
}
