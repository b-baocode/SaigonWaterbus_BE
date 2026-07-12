using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Maintenance;

/// <summary>
/// Xoa cac station nam XA duong song chi dinh (vd "Sông Sài Gòn") qua nguong MaxDistanceMeters.
/// - Preview = true: chi liet ke station se bi xoa kem khoang cach thuc, KHONG xoa.
/// - Station bi tham chieu (route stop / booking / landmark / phan cong / GPS session) se duoc giu lai (skip).
/// - Station khong co toa do khong do duoc khoang cach nen cung duoc giu lai.
/// Station khong co cot hinh hoc trong DB (chi co latitude/longitude) nen khoang cach duoc tinh trong C#:
/// chieu diem len tung doan cua polyline (mat phang cuc bo) roi do bang Haversine.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record DeleteStationsFarFromWaterwayCommand(
    string WaterwayName,
    double MaxDistanceMeters,
    bool Preview) : IRequest<DeleteStationsFarFromWaterwayResultDto>;

public sealed record FarStationDto(
    Guid StationId,
    string StationCode,
    string StationName,
    double DistanceMeters,
    bool IsReferenced);

public sealed record DeleteStationsFarFromWaterwayResultDto(
    string WaterwayName,
    double MaxDistanceMeters,
    bool Preview,
    int NearStations,
    int FarStations,
    int DeletedStations,
    int SkippedReferencedStations,
    int StationsWithoutCoordinates,
    IReadOnlyList<FarStationDto> FarStationDetails);

public sealed class DeleteStationsFarFromWaterwayCommandValidator
    : AbstractValidator<DeleteStationsFarFromWaterwayCommand>
{
    public DeleteStationsFarFromWaterwayCommandValidator()
    {
        RuleFor(x => x.WaterwayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MaxDistanceMeters).GreaterThan(0);
    }
}

public sealed class DeleteStationsFarFromWaterwayCommandHandler
    : IRequestHandler<DeleteStationsFarFromWaterwayCommand, DeleteStationsFarFromWaterwayResultDto>
{
    private readonly IApplicationDbContext _context;

    public DeleteStationsFarFromWaterwayCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<DeleteStationsFarFromWaterwayResultDto> Handle(
        DeleteStationsFarFromWaterwayCommand request,
        CancellationToken cancellationToken)
    {
        var waterwayName = request.WaterwayName.Trim();

        var lines = await _context.Set<WaterwaySegment>()
            .AsNoTracking()
            .Where(s => s.WaterwayName != null && s.WaterwayName == waterwayName)
            .Select(s => s.Geometry)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.WaterwayName),
                $"Khong tim thay waterway ten '{waterwayName}'. Kiem tra lai o GET /api/waterways.")]);
        }

        var stations = await _context.Set<Station>().ToListAsync(cancellationToken);

        var withoutCoordinates = 0;
        var near = 0;
        var farStations = new List<(Station Station, double DistanceMeters)>();

        foreach (var station in stations)
        {
            if (!station.Latitude.HasValue || !station.Longitude.HasValue)
            {
                withoutCoordinates++;
                continue;
            }

            var distance = MinDistanceMeters(
                (double)station.Latitude.Value,
                (double)station.Longitude.Value,
                lines);

            if (distance <= request.MaxDistanceMeters)
            {
                near++;
            }
            else
            {
                farStations.Add((station, distance));
            }
        }

        var referencedStationIds = await StationReferenceSupport.GetReferencedStationIdsAsync(
            _context, cancellationToken);

        var details = farStations
            .OrderBy(x => x.DistanceMeters)
            .Select(x => new FarStationDto(
                x.Station.Id,
                x.Station.StationCode,
                x.Station.StationName,
                Math.Round(x.DistanceMeters, 1),
                referencedStationIds.Contains(x.Station.Id)))
            .ToList();

        var deletable = farStations
            .Where(x => !referencedStationIds.Contains(x.Station.Id))
            .Select(x => x.Station)
            .ToList();

        var skippedReferenced = farStations.Count - deletable.Count;

        if (request.Preview)
        {
            return new DeleteStationsFarFromWaterwayResultDto(
                waterwayName, request.MaxDistanceMeters, Preview: true,
                near, farStations.Count,
                DeletedStations: 0,
                skippedReferenced,
                withoutCoordinates,
                details);
        }

        _context.Set<Station>().RemoveRange(deletable);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteStationsFarFromWaterwayResultDto(
            waterwayName, request.MaxDistanceMeters, Preview: false,
            near, farStations.Count,
            deletable.Count,
            skippedReferenced,
            withoutCoordinates,
            details);
    }

    /// <summary>Khoang cach ngan nhat (met) tu mot diem toi tap hop polyline.</summary>
    private static double MinDistanceMeters(double lat, double lon, IReadOnlyList<LineString> lines)
    {
        var min = double.MaxValue;

        foreach (var line in lines)
        {
            if (line.NumPoints == 0)
            {
                continue;
            }

            if (line.NumPoints == 1)
            {
                var only = line.GetCoordinateN(0);
                min = Math.Min(min, RouteGeoJsonImportSupport.HaversineMeters(lat, lon, only.Y, only.X));
                continue;
            }

            for (var i = 0; i < line.NumPoints - 1; i++)
            {
                var a = line.GetCoordinateN(i);
                var b = line.GetCoordinateN(i + 1);
                min = Math.Min(min, DistanceToSegmentMeters(lat, lon, a.Y, a.X, b.Y, b.X));
            }
        }

        return min;
    }

    /// <summary>
    /// Khoang cach tu diem toi mot doan thang: chieu sang mat phang cuc bo (equirectangular,
    /// scale kinh do theo cos(lat)) de tim diem gan nhat tren doan, roi do khoang cach that bang Haversine.
    /// </summary>
    private static double DistanceToSegmentMeters(
        double pLat, double pLon,
        double aLat, double aLon,
        double bLat, double bLon)
    {
        var kx = Math.Cos(aLat * Math.PI / 180);

        var bx = (bLon - aLon) * kx;
        var by = bLat - aLat;
        var px = (pLon - aLon) * kx;
        var py = pLat - aLat;

        var lengthSquared = (bx * bx) + (by * by);
        var t = lengthSquared <= 0
            ? 0
            : Math.Clamp(((px * bx) + (py * by)) / lengthSquared, 0, 1);

        var closestLat = aLat + (t * (bLat - aLat));
        var closestLon = aLon + (t * (bLon - aLon));

        return RouteGeoJsonImportSupport.HaversineMeters(pLat, pLon, closestLat, closestLon);
    }
}
