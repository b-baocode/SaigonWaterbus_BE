using System.Net;

namespace SaigonWaterbus.Infrastructure.Auth;

internal static class EmailRecipientResolver
{
    public static string Resolve(string? testRecipientEmail, string intendedRecipientEmail)
    {
        var testRecipient = testRecipientEmail?.Trim();
        return string.IsNullOrWhiteSpace(testRecipient)
            ? intendedRecipientEmail.Trim()
            : testRecipient;
    }

    public static bool IsRedirected(string? testRecipientEmail, string intendedRecipientEmail) =>
        !string.Equals(
            Resolve(testRecipientEmail, intendedRecipientEmail),
            intendedRecipientEmail.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public static void AddDebugParams(
        Dictionary<string, object?> parameters,
        string? testRecipientEmail,
        string intendedRecipientEmail)
    {
        var originalRecipientEmail = intendedRecipientEmail.Trim();
        var resolvedRecipientEmail = Resolve(testRecipientEmail, originalRecipientEmail);
        var isRedirected = !string.Equals(
            resolvedRecipientEmail,
            originalRecipientEmail,
            StringComparison.OrdinalIgnoreCase);

        parameters["recipientEmail"] = resolvedRecipientEmail;
        parameters["originalRecipientEmail"] = originalRecipientEmail;
        parameters["isTestRecipientRedirect"] = isRedirected;

        if (isRedirected)
        {
            parameters["testRecipientEmail"] = resolvedRecipientEmail;
        }
    }

    public static string AddOriginalRecipientNotice(
        string body,
        bool isHtml,
        string? testRecipientEmail,
        string intendedRecipientEmail)
    {
        if (!IsRedirected(testRecipientEmail, intendedRecipientEmail))
        {
            return body;
        }

        var originalRecipientEmail = intendedRecipientEmail.Trim();
        return isHtml
            ? $"<p><strong>Original recipient:</strong> {WebUtility.HtmlEncode(originalRecipientEmail)}</p>{body}"
            : $"Original recipient: {originalRecipientEmail}{Environment.NewLine}{Environment.NewLine}{body}";
    }
}
