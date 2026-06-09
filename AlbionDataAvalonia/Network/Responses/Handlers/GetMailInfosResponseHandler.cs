using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class GetMailInfosResponseHandler : ResponsePacketHandler<GetMailInfosResponse>
{
    private readonly PlayerState playerState;
    private readonly MailService mailService;
    public GetMailInfosResponseHandler(PlayerState playerState, MailService mailService) : base((int)OperationCodes.GetMailInfos)
    {
        this.playerState = playerState;
        this.mailService = mailService;
    }

    protected override async Task OnActionAsync(GetMailInfosResponse value)
    {
        List<AlbionMail> AlbionMails = new();

        if (playerState.AlbionServer == null || string.IsNullOrEmpty(playerState.PlayerName))
        {
            return;
        }

        for (int i = 0; i < value.MailIds.Length; i++)
        {
            var type = Enum.TryParse(typeof(AlbionMailInfoType), value.Types[i], true, out object? parsedType) ? (AlbionMailInfoType)parsedType : AlbionMailInfoType.UNKNOWN;

            // Only market/blackmarket summary mails carry a market location. Other mail
            // (system messages, player houses, guild halls, etc.) has no location to resolve.
            if (type == AlbionMailInfoType.UNKNOWN)
            {
                continue;
            }

            var rawLocationId = value.LocationIds[i];
            AlbionMail mail = new(value.MailIds[i], rawLocationId, playerState.PlayerName, type, new DateTime(value.Received[i]), playerState.AlbionServer.Id);

            if (mail.LocationId == AlbionLocations.Unknown.IdInt)
            {
                Log.Warning("Could not resolve mail location. MailId: {MailId}. RawLocationId: {RawLocationId}", value.MailIds[i], rawLocationId);
            }

            AlbionMails.Add(mail);
        }
        await mailService.AddMails(AlbionMails);

        await Task.CompletedTask;
    }
}
