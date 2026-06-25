using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Stations;

public sealed record CreateStationCommand(
    string StationCode,
    string StationName,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string? ImageUrl = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<StationImageFileRequest>? ImageFiles = null) : IRequest<StationDto>;

public sealed class CreateStationCommandValidator : AbstractValidator<CreateStationCommand>
{
    public CreateStationCommandValidator()
    {
        RuleFor(x => x.StationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(500).When(x => x.Address is not null);
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(StationImageSupport.IsValidImageUrl)
            .WithMessage("ImageUrl must be an absolute URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));

        RuleForEach(x => x.ImageUrls)
            .MaximumLength(2048)
            .Must(StationImageSupport.IsValidImageUrl)
            .WithMessage("ImageUrls must contain absolute URLs.");

        RuleFor(x => x)
            .Must(x => StationImageSupport.HasValidRequestedImageCount(x.ImageUrl, x.ImageUrls, x.ImageFiles))
            .WithMessage("Schema gọn chỉ lưu 1 ảnh chính cho mỗi bến.");
    }
}

public sealed class CreateStationCommandHandler : IRequestHandler<CreateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;

    public CreateStationCommandHandler(
        IApplicationDbContext context,
        IStationImageStorageService? stationImageStorage = null)
    {
        _context = context;
    }

    public async Task<StationDto> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var code = request.StationCode.Trim().ToUpperInvariant();

        if (await _context.Set<Station>().AnyAsync(s => s.StationCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.StationCode), "Station code already exists.")]);

        if (request.ImageFiles is { Count: > 0 })
        {
            throw AuthSupport.CreateValidationException(nameof(request.ImageFiles), "Schema gọn chỉ hỗ trợ imageUrl, không lưu nhiều ảnh bến.");
        }

        var imageUrl = StationImageSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).FirstOrDefault();
        var station = new Station
        {
            StationCode = code,
            StationName = request.StationName.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Status = StationStatus.Active,
            ImageUrl = imageUrl
        };

        _context.Set<Station>().Add(station);
        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }
}
