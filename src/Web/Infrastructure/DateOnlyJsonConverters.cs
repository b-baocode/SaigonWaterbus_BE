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
        "yyyy-MM-dd",
        "d/M/yyyy",
        "d-M-yyyy"
    ];

    private static readonly string[] AcceptedDateTimeFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFF",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFK",
        "yyyy-MM-ddTHH:mm:ss.fffK"
    ];

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Date must be a string in dd/MM/yyyy, dd-MM-yyyy or yyyy-MM-dd format.");
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

        if (DateTime.TryParseExact(
                value,
                AcceptedDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        throw new JsonException("Invalid date format. Use dd/MM/yyyy, dd-MM-yyyy or yyyy-MM-dd.");
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

internal sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] AcceptedFormats =
    [
        "HH:mm",
        "H:mm",
        "HH:mm:ss",
        "H:mm:ss"
    ];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Time must be a string in HH:mm format.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Time value is required.");
        }

        if (TimeOnly.TryParseExact(
                value,
                AcceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return time;
        }

        throw new JsonException("Invalid time format. Use HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}

internal sealed class NullableTimeOnlyJsonConverter : JsonConverter<TimeOnly?>
{
    private readonly TimeOnlyJsonConverter _innerConverter = new();

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _innerConverter.Read(ref reader, typeof(TimeOnly), options);
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        _innerConverter.Write(writer, value.Value, options);
    }
}
