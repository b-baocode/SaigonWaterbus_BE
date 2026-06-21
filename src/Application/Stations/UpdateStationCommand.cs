using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Stations;

public sealed record UpdateStationCommand(
    Guid StationId,
    string StationName,
    string? Address,
    string? Description,
    decimal? Latitude,
    decimal? Longitude,
    StationStatus Status,
    string? ImageUrl,
    IReadOnlyCollection<string>? ImageUrls,
    bool? HasWaitingArea,
    bool? HasParking,
    bool? HasTicketCounter,
    IReadOnlyCollection<StationImageFileRequest>? ImageFiles = null) : IRequest<StationDto>;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(300).When(x => x.Address is not null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
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

        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateStationCommandHandler : IRequestHandler<UpdateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStationImageStorageService _stationImageStorage;

    public UpdateStationCommandHandler(
        IApplicationDbContext context,
        IStationImageStorageService stationImageStorage)
    {
        _context = context;
        _stationImageStorage = stationImageStorage;
    }

    public async Task<StationDto> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .Include(s => s.Images)
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        station.StationName = request.StationName.Trim();
        station.Address = request.Address?.Trim() ?? station.Address;
        station.Description = request.Description?.Trim() ?? station.Description;
        station.Latitude = request.Latitude ?? station.Latitude;
        station.Longitude = request.Longitude ?? station.Longitude;
        station.Status = request.Status;

        var imageUrls = StationImageSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls);
        var imageFiles = request.ImageFiles ?? [];
        var hasImageUpdate = imageUrls.Count > 0 || imageFiles.Count > 0;

        foreach (var imageFile in imageFiles)
        {
            StationImageSupport.EnsureValidImage(
                "Image",
                imageFile.FileName,
                imageFile.ContentType,
                imageFile.Length,
                _stationImageStorage);
        }

        if (hasImageUpdate)
        {
            await ReplaceImagesAsync(station, imageUrls, imageFiles, cancellationToken);
        }

        station.HasWaitingArea = request.HasWaitingArea ?? station.HasWaitingArea;
        station.HasParking = request.HasParking ?? station.HasParking;
        station.HasTicketCounter = request.HasTicketCounter ?? station.HasTicketCounter;

        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }

    private async Task ReplaceImagesAsync(
        Station station,
        IReadOnlyCollection<string> imageUrls,
        IReadOnlyCollection<StationImageFileRequest> imageFiles,
        CancellationToken cancellationToken)
    {
        var existingImages = await _context.StationImages
            .Where(x => x.StationId == station.Id)
            .ToListAsync(cancellationToken);
        _context.StationImages.RemoveRange(existingImages);

        station.Images = new List<StationImage>();
        station.ImageUrl = null;
        station.ImagePublicId = null;

        var displayOrder = 1;
        foreach (var imageUrl in imageUrls)
        {
            AddImage(station, StationImageSupport.CreateImage(
                station.Id,
                imageUrl,
                displayOrder++,
                isPrimary: station.Images.Count == 0));
        }

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
            AddImage(station, stationImage);
        }

        StationImageSupport.SyncPrimaryImage(station);
    }

    private void AddImage(Station station, StationImage image)
    {
        image.StationId = station.Id;
        _context.StationImages.Add(image);
        station.Images.Add(image);
    }
}
