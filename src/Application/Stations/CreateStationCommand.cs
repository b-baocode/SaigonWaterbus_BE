using FluentValidation.Results;
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
            .WithMessage($"Station can have at most {StationImageSupport.MaxStationImages} images.");
    }
}

public sealed class CreateStationCommandHandler : IRequestHandler<CreateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStationImageStorageService _stationImageStorage;

    public CreateStationCommandHandler(
        IApplicationDbContext context,
        IStationImageStorageService stationImageStorage)
    {
        _context = context;
        _stationImageStorage = stationImageStorage;
    }

    public async Task<StationDto> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var code = request.StationCode.Trim().ToUpperInvariant();

        if (await _context.Set<Station>().AnyAsync(s => s.StationCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.StationCode), "Station code already exists.")]);

        var imageUrls = StationImageSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls);
        var imageFiles = request.ImageFiles ?? [];

        foreach (var imageFile in imageFiles)
        {
            StationImageSupport.EnsureValidImage(
                "Image",
                imageFile.FileName,
                imageFile.ContentType,
                imageFile.Length,
                _stationImageStorage);
        }

        var station = new Station
        {
            StationCode = code,
            StationName = request.StationName.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Status = StationStatus.Active
        };

        var displayOrder = 1;
        foreach (var imageUrl in imageUrls)
        {
            station.Images.Add(StationImageSupport.CreateImage(
                station.Id,
                imageUrl,
                displayOrder++,
                isPrimary: station.Images.Count == 0));
        }
        StationImageSupport.SyncPrimaryImage(station);

        _context.Set<Station>().Add(station);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var imageFile in imageFiles)
        {
            var stationImage = new StationImage
            {
                StationId = station.Id,
                DisplayOrder = displayOrder++,
                IsPrimary = station.Images.Count == 0
            };
            var uploadedImage = await _stationImageStorage.UploadImageAsync(
                new StationImageUpload(
                    station.Id,
                    imageFile.Content,
                    imageFile.FileName,
                    imageFile.ContentType,
                    stationImage.Id),
                cancellationToken);

            stationImage.Url = uploadedImage.Url;
            stationImage.PublicId = uploadedImage.PublicId;
            _context.StationImages.Add(stationImage);
            station.Images.Add(stationImage);
        }

        if (imageFiles.Count > 0)
        {
            StationImageSupport.SyncPrimaryImage(station);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return StationDto.From(station);
    }
}
