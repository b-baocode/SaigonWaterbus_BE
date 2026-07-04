namespace SaigonWaterbus.Application.Common.Security;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuthorizeAttribute : Attribute
{
    public string Roles { get; set; } = string.Empty;
}
