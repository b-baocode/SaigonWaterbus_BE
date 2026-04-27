using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaigonWaterbus.Web.Infrastructure;

internal sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private static readonly string[] AcceptedFormats =
    [
        "dd/MM/yyyy",
        "dd-MM-yyyy",
        "yyyy-MM-dd"
    ];

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Date must be a string in dd/MM/yyyy format.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Date value is required.");
        }

        if (DateOnly.TryParseExact(
                value,
                AcceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new JsonException("Invalid date format. Use dd/MM/yyyy.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
}

internal sealed class NullableDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    private readonly DateOnlyJsonConverter _innerConverter = new();

    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _innerConverter.Read(ref reader, typeof(DateOnly), options);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        _innerConverter.Write(writer, value.Value, options);
    }
}
