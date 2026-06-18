using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class TripSchedule : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public Guid VesselId { get; set; }

    // Giờ khởi hành (e.g. 07:00)
    public TimeOnly DepartureTime { get; set; }

    public RecurrenceType RecurrenceType { get; set; }

    // Dùng khi RecurrenceType = Weekly
    // Lưu các DayOfWeek cách nhau bởi dấu phẩy, vd "1,2,3,4,5" = Thứ 2-6
    public string? WeeklyDays { get; set; }

    // Dùng khi RecurrenceType = Monthly (ngày trong tháng, 1-31)
    public int? MonthlyDay { get; set; }

    // Sinh trip trước bao nhiêu ngày (admin có thể chỉnh)
    public int HorizonDays { get; set; } = 30;

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public Route Route { get; set; } = null!;
    public Vessel Vessel { get; set; } = null!;
    public ICollection<GeneratedTrip> GeneratedTrips { get; set; } = new List<GeneratedTrip>();
}
