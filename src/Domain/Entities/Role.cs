namespace SaigonWaterbus.Domain.Entities;

public class Role : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string SystemName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public ICollection<User> Users { get; set; } = new List<User>();
}
