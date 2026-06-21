using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingAvailability
{
    public static async Task<bool> HasMatchingAvailableVesselAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        CancellationToken cancellationToken)
    {
        var unavailableVesselIds = await GetUnavailableVesselIdsAsync(
            context,
            request,
            excludeRequestId: null,
            cancellationToken);
        var unavailableVesselIdArray = unavailableVesselIds.ToArray();

        return await CustomBookingVesselMatcher.Apply(
                context.Set<Vessel>().AsNoTracking(),
                request.RequestedNumberOfDecks,
                request.RequestedSeatSetupType,
                request.RentalUnit,
                request.PassengerCount)
            .Where(x => !unavailableVesselIdArray.Contains(x.Id))
            .AnyAsync(cancellationToken);
    }

    public static async Task<IReadOnlySet<Guid>> GetUnavailableVesselIdsAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid? excludeRequestId,
        CancellationToken cancellationToken) =>
        await CustomBookingVesselReservations.GetUnavailableVesselIdsAsync(
            context,
            request,
            excludeRequestId,
            DateTimeOffset.UtcNow,
            cancellationToken);

    public static async Task EnsureVesselAvailableAsync(
        IApplicationDbContext context,
        CustomBookingRequest request,
        Guid vesselId,
        CancellationToken cancellationToken) =>
        await CustomBookingVesselReservations.EnsureVesselAvailableAsync(
            context,
            request,
            vesselId,
            DateTimeOffset.UtcNow,
            cancellationToken);
}
