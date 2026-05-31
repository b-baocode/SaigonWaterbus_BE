namespace SaigonWaterbus.Application.Boats;

public sealed record BoatDto(
    Guid BoatId,
    string BoatCode,
    string BoatName,
    int Capacity,
    string BoatStatus,
    string? Description);

public sealed record SeatDto(
    Guid SeatId,
    Guid BoatId,
    string SeatNumber,
    string? SeatClass,
    int? SeatRow,
    int? SeatColumn,
    bool IsActive);
