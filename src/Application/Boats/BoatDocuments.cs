using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record BoatDocumentDto(
    Guid BoatId,
    BoatDocumentType Type,
    bool IsUploaded,
    Guid? Id,
    string? FileName,
    string? ContentType,
    long? FileSize,
    string? FileUrl,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    DateTimeOffset? UploadedAt,
    DateTimeOffset? UpdatedAt,
    bool RequiresRefresh);

public sealed record BoatDocumentFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed record GetBoatDocumentsRequest(Guid BoatId);

public sealed record UpdateBoatDocumentRequest(
    Guid BoatId,
    BoatDocumentType Type,
    BoatDocumentFileRequest File,
    DateOnly? IssuedDate = null,
    DateOnly? ExpiryDate = null);

public sealed record DeleteBoatDocumentRequest(
    Guid BoatId,
    BoatDocumentType Type);

public sealed class GetBoatDocumentsRequestValidator : AbstractValidator<GetBoatDocumentsRequest>
{
    public GetBoatDocumentsRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");
    }
}

public sealed class UpdateBoatDocumentRequestValidator : AbstractValidator<UpdateBoatDocumentRequest>
{
    public UpdateBoatDocumentRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Loại hồ sơ tàu không hợp lệ.");

        RuleFor(x => x.File.FileName)
            .NotEmpty()
            .WithMessage("Tên file hồ sơ tàu là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Tên file hồ sơ tàu không được vượt quá 255 ký tự.");

        RuleFor(x => x.File.Length)
            .GreaterThan(0)
            .WithMessage("File hồ sơ tàu là bắt buộc.");

        RuleFor(x => x)
            .Must(x => !x.IssuedDate.HasValue
                || !x.ExpiryDate.HasValue
                || x.ExpiryDate.Value >= x.IssuedDate.Value)
            .WithMessage("Ngày hết hạn hồ sơ tàu phải lớn hơn hoặc bằng ngày cấp.");
    }
}

public sealed class DeleteBoatDocumentRequestValidator : AbstractValidator<DeleteBoatDocumentRequest>
{
    public DeleteBoatDocumentRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Loại hồ sơ tàu không hợp lệ.");
    }
}

public sealed class GetBoatDocumentsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatDocumentStorageService? _boatDocumentStorage;

    public GetBoatDocumentsRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        IBoatDocumentStorageService? boatDocumentStorage = null)
    {
        _context = context;
        _userContext = userContext;
        _boatDocumentStorage = boatDocumentStorage;
    }

    public async Task<IReadOnlyCollection<BoatDocumentDto>> ExecuteAsync(
        GetBoatDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatSupport.EnsureCurrentUserCanViewBoatsAsync(_context, _userContext, cancellationToken);
        var boat = await BoatSupport.ApplyVisibilityFilter(
                _context.Boats.AsNoTracking(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        return BoatDocumentSupport.CreateDocumentSlots(boat, _boatDocumentStorage);
    }
}

public sealed class UpdateBoatDocumentRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatDocumentStorageService? _boatDocumentStorage;
    private readonly TimeProvider _timeProvider;

    public UpdateBoatDocumentRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBoatDocumentStorageService? boatDocumentStorage = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _boatDocumentStorage = boatDocumentStorage;
    }

    public async Task<BoatDocumentDto> ExecuteAsync(
        UpdateBoatDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var storage = _boatDocumentStorage
            ?? throw AuthSupport.CreateValidationException(nameof(request.File), "Dịch vụ lưu hồ sơ tàu chưa được cấu hình.");
        BoatDocumentSupport.EnsureValidDocument(request.File, storage);

        if (request.File.Content.CanSeek)
        {
            request.File.Content.Position = 0;
        }

        var documentId = Guid.NewGuid();
        var storedDocument = await storage.UploadDocumentAsync(
            new BoatDocumentUpload(
                boat.Id,
                request.Type,
                documentId,
                request.File.Content,
                request.File.FileName,
                request.File.ContentType),
            cancellationToken);

        var document = new BoatDocument
        {
            Id = documentId,
            Type = request.Type,
            FileName = request.File.FileName.Trim(),
            ContentType = request.File.ContentType?.Trim() ?? string.Empty,
            FileSize = request.File.Length,
            FileUrl = storedDocument.Url,
            StorageKey = storedDocument.StorageKey,
            IssuedDate = request.IssuedDate,
            ExpiryDate = request.ExpiryDate,
            UploadedAt = _timeProvider.GetUtcNow()
        };

        boat.Documents = BoatDocumentSupport.ReplaceDocument(boat.Documents, document);
        BoatDocumentSupport.AutoActivateIfReady(boat);
        await _context.SaveChangesAsync(cancellationToken);

        return BoatDocumentSupport.CreateDocumentDto(
            boat.Id,
            request.Type,
            document,
            boat.MaintenanceStartedAt,
            _boatDocumentStorage);
    }
}

public sealed class DeleteBoatDocumentRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteBoatDocumentRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BoatDocumentDto> ExecuteAsync(
        DeleteBoatDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        boat.Documents = BoatDocumentSupport.RemoveDocument(boat.Documents, request.Type);
        if (boat.Status == BoatStatus.Active)
        {
            boat.Status = BoatStatus.Inactive;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BoatDocumentSupport.CreateDocumentDto(boat.Id, request.Type, null, boat.MaintenanceStartedAt);
    }
}

internal static class BoatDocumentSupport
{
    public static readonly BoatDocumentType[] RequiredDocumentTypes =
    [
        BoatDocumentType.Inspection,
        BoatDocumentType.Registration,
        BoatDocumentType.Insurance,
        BoatDocumentType.OperationLicense
    ];

    public static IReadOnlyCollection<BoatDocumentDto> CreateDocumentSlots(Boat boat)
    {
        return CreateDocumentSlots(boat, null);
    }

    public static IReadOnlyCollection<BoatDocumentDto> CreateDocumentSlots(
        Boat boat,
        IBoatDocumentStorageService? boatDocumentStorage)
    {
        var latestDocuments = CreateLatestDocumentMap(boat.Documents);
        return RequiredDocumentTypes
            .Select(type => CreateDocumentDto(
                boat.Id,
                type,
                latestDocuments.GetValueOrDefault(type),
                boat.MaintenanceStartedAt,
                boatDocumentStorage))
            .ToArray();
    }

    public static BoatDocumentDto CreateDocumentDto(
        Guid boatId,
        BoatDocumentType type,
        BoatDocument? document,
        DateTimeOffset? maintenanceStartedAt,
        IBoatDocumentStorageService? boatDocumentStorage = null)
    {
        var fileUrl = CreateDocumentUrl(document, boatDocumentStorage);
        return new BoatDocumentDto(
            boatId,
            type,
            document is not null,
            document?.Id,
            document?.FileName,
            document?.ContentType,
            document?.FileSize,
            fileUrl,
            document?.IssuedDate,
            document?.ExpiryDate,
            document?.UploadedAt,
            document?.UploadedAt,
            RequiresRefresh(type, document, maintenanceStartedAt));
    }

    public static BoatDocument[] ReplaceDocument(
        IReadOnlyCollection<BoatDocument>? documents,
        BoatDocument document)
    {
        var retainedDocuments = (documents ?? [])
            .Where(x => x.Type != document.Type)
            .Append(document);

        return OrderDocuments(retainedDocuments);
    }

    public static BoatDocument[] RemoveDocument(
        IReadOnlyCollection<BoatDocument>? documents,
        BoatDocumentType type) =>
        OrderDocuments((documents ?? []).Where(x => x.Type != type));

    public static void EnsureCanActivate(Boat boat)
    {
        if (!HasRequiredDocumentsForActivation(boat, out var missingDocumentTypes))
        {
            throw AuthSupport.CreateValidationException(
                "Documents",
                $"Tàu cần upload đủ 4 hồ sơ trước khi chuyển Active. Thiếu: {string.Join(", ", missingDocumentTypes)}.");
        }

        if (!HasFreshInspectionAfterMaintenance(boat))
        {
            throw AuthSupport.CreateValidationException(
                "Documents",
                "Tàu đã vào bảo trì, cần cập nhật lại hồ sơ đăng kiểm trước khi chuyển Active.");
        }
    }

    public static void AutoActivateIfReady(Boat boat)
    {
        if (boat.Status is not BoatStatus.Inactive and not BoatStatus.UnderMaintenance)
        {
            return;
        }

        if (BoatSupport.IsReadyForActivation(boat)
            && HasRequiredDocumentsForActivation(boat, out _)
            && HasFreshInspectionAfterMaintenance(boat))
        {
            boat.Status = BoatStatus.Active;
        }
    }

    public static bool RequiresDocumentRefresh(Boat boat) =>
        boat.MaintenanceStartedAt is not null
        && !HasFreshInspectionAfterMaintenance(boat);

    public static bool RequiresRefresh(
        BoatDocumentType type,
        BoatDocument? document,
        DateTimeOffset? maintenanceStartedAt) =>
        type == BoatDocumentType.Inspection
        && maintenanceStartedAt is not null
        && (document is null || document.UploadedAt <= maintenanceStartedAt.Value);

    public static void EnsureValidDocument(
        BoatDocumentFileRequest file,
        IBoatDocumentStorageService storage)
    {
        if (file.Length > storage.MaxDocumentBytes)
        {
            throw AuthSupport.CreateValidationException(
                nameof(file.Length),
                $"Hồ sơ tàu không được vượt quá {storage.MaxDocumentBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType)
            || !storage.AllowedDocumentContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                nameof(file.ContentType),
                "Hồ sơ tàu chỉ hỗ trợ PDF, JPEG, PNG hoặc WebP.");
        }
    }

    private static string? CreateDocumentUrl(
        BoatDocument? document,
        IBoatDocumentStorageService? boatDocumentStorage)
    {
        if (document is null)
        {
            return null;
        }

        if (boatDocumentStorage is not null && !string.IsNullOrWhiteSpace(document.StorageKey))
        {
            try
            {
                var signedUrl = boatDocumentStorage.CreateDocumentUrl(document.StorageKey);
                if (!string.IsNullOrWhiteSpace(signedUrl))
                {
                    return signedUrl;
                }
            }
            catch
            {
                return document.FileUrl;
            }
        }

        return document.FileUrl;
    }

    private static bool HasRequiredDocumentsForActivation(
        Boat boat,
        out BoatDocumentType[] missingDocumentTypes)
    {
        var latestDocuments = CreateLatestDocumentMap(boat.Documents);
        missingDocumentTypes = RequiredDocumentTypes
            .Where(type => !latestDocuments.ContainsKey(type))
            .ToArray();

        return missingDocumentTypes.Length == 0;
    }

    private static bool HasFreshInspectionAfterMaintenance(Boat boat)
    {
        if (boat.MaintenanceStartedAt is null)
        {
            return true;
        }

        var latestDocuments = CreateLatestDocumentMap(boat.Documents);
        return latestDocuments.TryGetValue(BoatDocumentType.Inspection, out var inspectionDocument)
            && inspectionDocument.UploadedAt > boat.MaintenanceStartedAt.Value;
    }

    private static Dictionary<BoatDocumentType, BoatDocument> CreateLatestDocumentMap(
        IReadOnlyCollection<BoatDocument>? documents) =>
        (documents ?? [])
        .Where(x => RequiredDocumentTypes.Contains(x.Type))
        .GroupBy(x => x.Type)
        .ToDictionary(
            group => group.Key,
            group => group
                .OrderByDescending(x => x.UploadedAt)
                .ThenByDescending(x => x.Id)
                .First());

    private static BoatDocument[] OrderDocuments(IEnumerable<BoatDocument> documents) =>
        documents
            .Where(x => RequiredDocumentTypes.Contains(x.Type))
            .OrderBy(x => Array.IndexOf(RequiredDocumentTypes, x.Type))
            .ToArray();
}
