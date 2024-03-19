using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Pow;

public class PowRequest
{
    [JsonPropertyName("key")]
    public string Key { get; set; }
    [JsonPropertyName("wanted")]
    public string Wanted { get; set; }
}
