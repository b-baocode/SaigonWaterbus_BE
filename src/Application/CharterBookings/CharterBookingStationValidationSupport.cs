using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingStationValidationSupport
{
    public static async Task EnsureWaterbusDepartureStationAsync(
        IApplicationDbContext context,
        Guid? stationId,
        string field,
        CancellationToken cancellationToken)
    {
        if (!stationId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(field, "Bến bắt đầu là bắt buộc.")]);
        }

        var station = await context.Set<Station>()
            .AsNoTracking()
            .Where(s => s.Id == stationId.Value)
            .Select(s => new { s.IsWaterbusStation, s.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (station is null)
        {
            throw new ValidationException([new ValidationFailure(field, "Bến bắt đầu không tồn tại.")]);
        }

        if (station.Status != StationStatus.Active || !station.IsWaterbusStation)
        {
            throw new ValidationException([new ValidationFailure(field,
                "Bến bắt đầu phải là bến Waterbus đang hoạt động.")]);
        }
    }
}
