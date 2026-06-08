using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Stations;

public sealed record StationDto(
    Guid StationId,
    string StationCode,
    string StationName,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string Status)
{
    public static StationDto From(Station s) => new(
        s.Id, s.StationCode, s.StationName,
        s.Address, s.Latitude, s.Longitude, s.Status.ToString());
}
