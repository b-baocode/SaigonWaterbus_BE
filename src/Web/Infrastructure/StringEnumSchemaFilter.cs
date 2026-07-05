using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaigonWaterbus.Web.Infrastructure;

internal sealed class StringEnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var enumType = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!enumType.IsEnum)
        {
            return;
        }

        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(enumType)
            .Select(name => (IOpenApiAny)new OpenApiString(name))
            .ToList();
    }
}

internal sealed class TimeOnlySchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var valueType = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (valueType == typeof(TimeOnly))
        {
            ApplyTimeSchema(schema, Nullable.GetUnderlyingType(context.Type) is not null);
        }

        foreach (var property in context.Type.GetProperties())
        {
            var propertyValueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyValueType != typeof(TimeOnly))
            {
                continue;
            }

            var propertyName = property
                .GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: true)
                .OfType<JsonPropertyNameAttribute>()
                .FirstOrDefault()
                ?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);

            if (schema.Properties.TryGetValue(propertyName, out var propertySchema))
            {
                ApplyTimeSchema(propertySchema, Nullable.GetUnderlyingType(property.PropertyType) is not null);
            }
        }
    }

    private static void ApplyTimeSchema(OpenApiSchema schema, bool nullable)
    {
        schema.Type = "string";
        schema.Format = "time";
        schema.Nullable = nullable;
        schema.Example = new OpenApiString("08:00:00");
    }
}

internal sealed class TimeOnlyDocumentFilter : IDocumentFilter
{
    private static readonly HashSet<string> TimeOnlyPropertyNames =
    [
        "startTime",
        "openingTime",
        "closingTime"
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var schema in swaggerDoc.Components.Schemas.Values)
        {
            foreach (var (propertyName, propertySchema) in schema.Properties)
            {
                if (!TimeOnlyPropertyNames.Contains(propertyName))
                {
                    continue;
                }

                propertySchema.Type = "string";
                propertySchema.Format = "time";
                propertySchema.Nullable = true;
                propertySchema.Example = new OpenApiString("08:00:00");
            }
        }
    }
}
