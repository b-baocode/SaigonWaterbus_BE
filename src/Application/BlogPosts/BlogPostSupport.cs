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
    public const string ActivityCategory = "Activity";
    public const string EventCategory = "Event";
    public const string NewsCategory = "News";

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

    public static bool IsValidCategory(string? category)
    {
        var normalized = category?.Trim();
        return string.Equals(normalized, ActivityCategory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, EventCategory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, NewsCategory, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeCategory(string? category, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw CreateValidationException(propertyName, "Category bat buoc nhap. Gia tri hop le: Activity | Event | News.");
        }

        var normalized = category.Trim();
        if (string.Equals(normalized, ActivityCategory, StringComparison.OrdinalIgnoreCase))
        {
            return ActivityCategory;
        }

        if (string.Equals(normalized, EventCategory, StringComparison.OrdinalIgnoreCase))
        {
            return EventCategory;
        }

        if (string.Equals(normalized, NewsCategory, StringComparison.OrdinalIgnoreCase))
        {
            return NewsCategory;
        }

        throw CreateValidationException(propertyName, "Category hop le: Activity | Event | News.");
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

    public static void EnsureValidImageFile(
        string propertyPrefix,
        string? fileName,
        string? contentType,
        long? length,
        IBlogImageStorageService blogImageStorage)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw CreateValidationException($"{propertyPrefix}FileName", "Tên file ảnh blog là bắt buộc.");
        }

        if (!length.HasValue || length <= 0)
        {
            throw CreateValidationException($"{propertyPrefix}Length", "Ảnh blog là bắt buộc.");
        }

        if (length > blogImageStorage.MaxImageBytes)
        {
            throw CreateValidationException(
                $"{propertyPrefix}Length",
                $"Ảnh blog không được vượt quá {blogImageStorage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !blogImageStorage.AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw CreateValidationException(
                $"{propertyPrefix}ContentType",
                "Ảnh blog chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }

    public static async Task<string> UploadImageAsync(
        Guid blogPostId,
        BlogPostImageFileRequest imageFile,
        IBlogImageStorageService? blogImageStorage,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (blogImageStorage is null)
        {
            throw CreateValidationException(propertyName, "Dịch vụ lưu ảnh blog chưa được cấu hình.");
        }

        EnsureValidImageFile(
            propertyName,
            imageFile.FileName,
            imageFile.ContentType,
            imageFile.Length,
            blogImageStorage);

        if (imageFile.Content.CanSeek)
        {
            imageFile.Content.Position = 0;
        }

        var storedImage = await blogImageStorage.UploadImageAsync(
            new BlogImageUpload(
                blogPostId,
                imageFile.Content,
                imageFile.FileName,
                imageFile.ContentType,
                Guid.NewGuid()),
            cancellationToken);

        return storedImage.Url;
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
            post.Category,
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

    public static SaigonWaterbus.Application.Common.Exceptions.ValidationException CreateValidationException(
        string propertyName,
        string errorMessage) =>
        new([new ValidationFailure(propertyName, errorMessage)]);
}
