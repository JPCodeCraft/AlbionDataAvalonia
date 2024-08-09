using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuctionType
{
    unknown,
    offer,
    request
}
