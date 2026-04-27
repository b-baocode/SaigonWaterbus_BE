namespace SaigonWaterbus.Web.Infrastructure;

internal static class OpenApiDescriptionBuilder
{
    public static string Build(string access, string? example = null, params string[] notes)
    {
        var lines = new List<string> { $"Quyen truy cap: {access}." };

        if (!string.IsNullOrWhiteSpace(example))
        {
            lines.Add(string.Empty);
            lines.Add("Example body:");
            lines.Add(example.Trim());
        }

        if (notes.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Note:");
            lines.AddRange(notes.Select(note => $"- {note}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
