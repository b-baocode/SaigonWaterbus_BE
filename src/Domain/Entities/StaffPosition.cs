namespace SaigonWaterbus.Domain.Entities;

public class StaffPosition : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string SystemName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public ICollection<UserPosition> UserPositions { get; set; } = new List<UserPosition>();
}
