namespace SaigonWaterbus.Domain.Constants;

public abstract class Roles
{
    public const string Administrator = nameof(Administrator);
    public const string Manager = nameof(Manager);
    public const string Staff = nameof(Staff);
    public const string Customer = nameof(Customer);

    public static readonly IReadOnlyList<string> All = [Administrator, Manager, Staff, Customer];
}
