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
    IReadOnlyCollection<StationImageFileRequest>? ImageFiles = null,
    TimeOnly? OpeningTime = null,
    TimeOnly? ClosingTime = null,
    bool? IsWaterbusStation = null) : IRequest<StationDto>;

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
            .WithMessage($"Mỗi bến chỉ được lưu tối đa {StationImageSupport.MaxStationImages} ảnh.");
    }
}

public sealed class CreateStationCommandHandler : IRequestHandler<CreateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStationImageStorageService? _stationImageStorage;

    public CreateStationCommandHandler(
        IApplicationDbContext context,
        IStationImageStorageService? stationImageStorage = null)
    {
        _context = context;
        _stationImageStorage = stationImageStorage;
    }

    public async Task<StationDto> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var code = request.StationCode.Trim().ToUpperInvariant();

        if (await _context.Set<Station>().AnyAsync(s => s.StationCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.StationCode), "Station code already exists.")]);

        var station = new Station
        {
            StationCode = code,
            StationName = request.StationName.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            OpeningTime = request.OpeningTime,
            ClosingTime = request.ClosingTime,
            IsWaterbusStation = request.IsWaterbusStation ?? true,
            Status = StationStatus.Active
        };

        var imageUrls = StationImageSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).ToList();
        var uploadedImages = await StationImageSupport.UploadImagesAsync(
            station.Id,
            request.ImageFiles,
            _stationImageStorage,
            nameof(request.ImageFiles),
            cancellationToken);
        imageUrls.AddRange(uploadedImages.Select(image => image.Url));
        StationImageSupport.ReplaceImages(station, imageUrls);

        _context.Set<Station>().Add(station);
        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }
}
