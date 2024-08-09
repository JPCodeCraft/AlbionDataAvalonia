using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionTopItems.Models.Converters;

public class TicksJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new DateTime(reader.GetInt64(), DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Ticks);
    }
}