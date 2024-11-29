using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionTopItems.Models.Converters;

public class LocationConverter : JsonConverter<AlbionLocation>
{
    public override AlbionLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            int id = reader.GetInt32();
            var location = AlbionLocations.Get(id);
            return location ?? AlbionLocations.Unknown;
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            string name = reader.GetString() ?? "";
            if (int.TryParse(name, out int id))
            {
                //Console.WriteLine("GOT ID: " + id + " AS STRING!!!!!!!!!!");
                return AlbionLocations.Get(id) ?? AlbionLocations.Unknown;
            }
            return AlbionLocations.Get(name) ?? AlbionLocations.Unknown;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, AlbionLocation value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Id", value.Id);
        writer.WriteString("Name", value.Name);
        writer.WriteEndObject();
    }
}
