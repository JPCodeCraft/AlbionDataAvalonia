using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class BankVaultInfoEvent : BaseEvent
    {
        public readonly long? _objectId;
        public readonly string _locationGuid = string.Empty;
        public readonly Guid[] _vaultGuidList = Array.Empty<Guid>();
        public readonly string[] _vaultNames = Array.Empty<string>();
        public readonly string[] _iconTags = Array.Empty<string>();

        public BankVaultInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object? objectId))
                {
                    _objectId = objectId.ToLong();
                }

                if (parameters.TryGetValue(1, out object? locationGuid))
                {
                    _locationGuid = locationGuid.ToString() ?? string.Empty;
                }

                if (parameters.TryGetValue(2, out object? vaultGuidList))
                {
                    _vaultGuidList = vaultGuidList.ToGuidArray() ?? Array.Empty<Guid>();
                }

                if (parameters.TryGetValue(3, out object? vaultNames))
                {
                    _vaultNames = vaultNames.ToStringArray() ?? Array.Empty<string>();
                }

                if (parameters.TryGetValue(4, out object? iconTags))
                {
                    _iconTags = iconTags.ToStringArray() ?? Array.Empty<string>();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
