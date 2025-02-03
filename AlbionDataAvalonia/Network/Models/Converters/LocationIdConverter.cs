using AlbionDataAvalonia.Locations;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionTopItems.Models.Converters
{
    public class LocationIdConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? stringValue = reader.GetString();
                return AlbionLocations.GetIdInt(stringValue);
            }
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }
            return null;
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(value.Value.ToString());
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
