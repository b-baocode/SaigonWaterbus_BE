using SaigonWaterbus.Application.Auth.Common;
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
    IReadOnlyCollection<StationImageFileRequest>? ImageFiles = null,
    StationType? StationType = null,
    string? OperatingHours = null,
    bool? IsWaterbusStation = null) : IRequest<StationDto>;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(300).When(x => x.Address is not null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.StationType).IsInEnum().When(x => x.StationType.HasValue);
        RuleFor(x => x.OperatingHours).MaximumLength(150).When(x => x.OperatingHours is not null);
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

        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateStationCommandHandler : IRequestHandler<UpdateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStationImageStorageService? _stationImageStorage;

    public UpdateStationCommandHandler(
        IApplicationDbContext context,
        IStationImageStorageService? stationImageStorage = null)
    {
        _context = context;
        _stationImageStorage = stationImageStorage;
    }

    public async Task<StationDto> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        station.StationName = request.StationName.Trim();
        station.Address = request.Address?.Trim() ?? station.Address;
        station.Description = request.Description?.Trim() ?? station.Description;
        station.Latitude = request.Latitude ?? station.Latitude;
        station.Longitude = request.Longitude ?? station.Longitude;
        station.StationType = request.StationType ?? station.StationType;
        if (request.OperatingHours is not null)
        {
            station.OperatingHours = NormalizeOptionalText(request.OperatingHours);
        }
        station.IsWaterbusStation = request.IsWaterbusStation ?? station.IsWaterbusStation;
        station.Status = request.Status;

        var imageUrls = StationImageSupport.NormalizeImageUrls(request.ImageUrl, request.ImageUrls).ToList();
        var uploadedImages = await StationImageSupport.UploadImagesAsync(
            station.Id,
            request.ImageFiles,
            _stationImageStorage,
            nameof(request.ImageFiles),
            cancellationToken);
        imageUrls.AddRange(uploadedImages.Select(image => image.Url));
        if (imageUrls.Count > 0)
        {
            StationImageSupport.ReplaceImages(station, imageUrls);
        }

        station.HasWaitingArea = request.HasWaitingArea ?? station.HasWaitingArea;
        station.HasParking = request.HasParking ?? station.HasParking;
        station.HasTicketCounter = request.HasTicketCounter ?? station.HasTicketCounter;

        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
