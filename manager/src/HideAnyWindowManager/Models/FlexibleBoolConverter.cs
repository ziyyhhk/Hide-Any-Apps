using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HideAnyWindowManager.Models;

/// <summary>
/// Accepts both JSON booleans and the 0/1 numbers written by the AHK service
/// (AutoHotkey has no distinct boolean type, so its JSON library serializes
/// <c>true</c>/<c>false</c> as 1/0). Without this, the manager treated every
/// service-written config as corrupt and fell back to an empty rule list.
/// Always writes real JSON booleans.
/// </summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out long number))
                    return number != 0;
                break;
            case JsonTokenType.String:
                string? text = reader.GetString();
                if (bool.TryParse(text, out bool parsed))
                    return parsed;
                if (text == "1") return true;
                if (text == "0") return false;
                break;
        }

        throw new JsonException($"Cannot convert {reader.TokenType} to a boolean.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}
