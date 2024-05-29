using System;

namespace AlbionDataAvalonia.Network.Models
{
    public class BaseUpload
    {
        public Guid Identifier { get; } = Guid.NewGuid();
    }
}