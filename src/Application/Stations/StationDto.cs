using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
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
    TimeOnly? OpeningTime,
    TimeOnly? ClosingTime,
    bool IsWaterbusStation,
    string? Address,
    string? Description,
    decimal? Latitude,
    decimal? Longitude,
    string Status,
    string? ImageUrl,
    IReadOnlyCollection<string> ImageUrls,
    bool HasWaitingArea,
    bool HasParking,
    bool HasTicketCounter,
    IReadOnlyCollection<StationUserAssignmentDto> Managers,
    IReadOnlyCollection<StationUserAssignmentDto> Staff)
{
    public static StationDto From(Station s)
    {
        var imageUrls = StationImageSupport.CreateImageUrls(s);
        return new StationDto(
            s.Id, s.StationCode, s.StationName, s.OpeningTime, s.ClosingTime, s.IsWaterbusStation,
            s.Address, s.Description, s.Latitude, s.Longitude, s.Status.ToString(),
            imageUrls.FirstOrDefault(), imageUrls, s.HasWaitingArea, s.HasParking, s.HasTicketCounter,
            CreateAssignedUserDtos(s, Roles.ManagerSystemName),
            CreateAssignedUserDtos(s, Roles.StaffSystemName));
    }

    private static IReadOnlyCollection<StationUserAssignmentDto> CreateAssignedUserDtos(
        Station station,
        string roleSystemName) =>
        station.UserAssignments
            .Where(a => a.IsActive
                && a.User.Status == UserStatus.Active
                && string.Equals(a.User.Role.SystemName, roleSystemName, StringComparison.Ordinal))
            .OrderByDescending(a => a.IsPrimary)
            .ThenBy(a => a.User.FullName)
            .ThenBy(a => a.User.Id)
            .Select(a => new StationUserAssignmentDto(
                a.UserId,
                a.User.UserCode,
                a.User.FullName,
                a.User.PhoneNumber,
                a.User.Email,
                a.IsPrimary))
            .ToArray();
}
