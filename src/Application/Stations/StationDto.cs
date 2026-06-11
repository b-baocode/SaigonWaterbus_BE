using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Stations;

public sealed record StationDto(
    Guid StationId,
    string StationCode,
    string StationName,
    string? Address,
    string? Description,
    decimal? Latitude,
    decimal? Longitude,
    string Status,
    string? ImageUrl,
    bool HasWaitingArea,
    bool HasParking,
    bool HasTicketCounter)
{
    public static StationDto From(Station s) => new(
        s.Id, s.StationCode, s.StationName,
        s.Address, s.Description, s.Latitude, s.Longitude, s.Status.ToString(),
        s.ImageUrl, s.HasWaitingArea, s.HasParking, s.HasTicketCounter);
}
