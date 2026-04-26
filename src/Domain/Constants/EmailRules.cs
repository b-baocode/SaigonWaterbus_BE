namespace SaigonWaterbus.Domain.Constants;

public abstract class EmailRules
{
    public static readonly IReadOnlyList<string> AllowedRegistrationDomains =
        ["gmail.com", "fpt.edu.vn"];

    public static bool HasAllowedRegistrationDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
        return AllowedRegistrationDomains.Contains(domain);
    }
}
