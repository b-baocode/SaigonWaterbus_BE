namespace SaigonWaterbus.Domain.Entities;

public class Role : BaseAuditableEntity
{
    private string? _systemName;

    public string Code { get; set; } = null!;

    public string SystemName
    {
        get => _systemName ?? Code switch
        {
            Roles.CustomerCode => Roles.CustomerSystemName,
            Roles.ManagerCode => Roles.ManagerSystemName,
            Roles.AdminCode => Roles.AdminName,
            Roles.StaffCode => Roles.StaffSystemName,
            _ => Code
        };
        set => _systemName = value;
    }

    public string DisplayName { get; set; } = null!;

    public string? Description { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
