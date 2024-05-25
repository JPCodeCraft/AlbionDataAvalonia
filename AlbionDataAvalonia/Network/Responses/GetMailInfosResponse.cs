using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class GetMailInfosResponse : BaseOperation
{
    public List<AlbionMail> AlbionMails { get; set; } = new();
    public GetMailInfosResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());
        try
        {
            long[] mailIds = [];
            string[] locationIds = [];
            string[] types = [];
            long[] expires = [];

            if (parameters.TryGetValue(3, out object? _ids))
            {
                mailIds = (long[])_ids;
            }
            if (parameters.TryGetValue(6, out object? _locationIds))
            {
                locationIds = (string[])_locationIds;
            }
            if (parameters.TryGetValue(10, out object? _types))
            {
                types = (string[])_types;
            }
            if (parameters.TryGetValue(11, out object? _expires))
            {
                expires = (long[])_expires;
            }

            for (int i = 0; i < mailIds.Length; i++)
            {
                var location = AlbionLocations.TryParse(locationIds[i], out var loc) ? loc ?? AlbionLocations.Unknown : AlbionLocations.Unknown;
                AlbionMail mail = new()
                {
                    Id = mailIds[i],
                    LocationId = location.Id,
                    Type = Enum.TryParse(typeof(MailInfoType), types[i], true, out object? parsedType) ? (MailInfoType)parsedType : MailInfoType.UNKNOWN,
                    Expires = new DateTime(expires[i])
                };
                if (mail.Type != MailInfoType.UNKNOWN)
                {
                    AlbionMails.Add(mail);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
