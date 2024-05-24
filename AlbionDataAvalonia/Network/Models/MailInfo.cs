using AlbionTopItems.Models.Converters;
using System;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models;

public class MailInfo
{
    public long MailId { get; set; }
    [JsonConverter(typeof(LocationConverter))]
    public AlbionLocation Location { get; set; } = AlbionLocations.Unset;
    public MailInfoType Type { get; set; }
    [JsonConverter(typeof(TicksJsonConverter))]
    public DateTime Expires { get; set; }
}
