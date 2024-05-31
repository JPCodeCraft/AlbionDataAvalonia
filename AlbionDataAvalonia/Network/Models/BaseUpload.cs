using System;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models
{
    public class BaseUpload
    {
        [JsonIgnore]
        public Guid Identifier { get; } = Guid.NewGuid();
    }
}