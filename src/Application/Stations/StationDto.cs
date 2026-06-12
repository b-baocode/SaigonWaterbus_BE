using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Stations;

public sealed record StationUserAssignmentDto(
    Guid UserId,
    string? UserCode,
    string FullName,
    string? PhoneNumber,
    string? Email,
    bool IsPrimary);

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
    bool HasTicketCounter,
    IReadOnlyCollection<StationUserAssignmentDto> Managers,
    IReadOnlyCollection<StationUserAssignmentDto> Staff)
{
    public static StationDto From(Station s) => new(
        s.Id, s.StationCode, s.StationName,
        s.Address, s.Description, s.Latitude, s.Longitude, s.Status.ToString(),
        s.ImageUrl, s.HasWaitingArea, s.HasParking, s.HasTicketCounter,
        CreateAssignedUserDtos(s, Roles.ManagerSystemName),
        CreateAssignedUserDtos(s, Roles.StaffSystemName));

    private static IReadOnlyCollection<StationUserAssignmentDto> CreateAssignedUserDtos(
        Station station,
        string roleSystemName) =>
        station.UserAssignments
            .Where(x => x.IsActive
                        && x.User.Status == UserStatus.Active
                        && x.User.Role.SystemName == roleSystemName)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.User.FullName)
            .Select(x => new StationUserAssignmentDto(
                x.UserId,
                x.User.UserCode,
                x.User.FullName,
                x.User.PhoneNumber,
                x.User.Email,
                x.IsPrimary))
            .ToArray();
}
