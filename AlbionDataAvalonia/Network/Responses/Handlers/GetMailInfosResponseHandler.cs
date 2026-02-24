using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
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
            if (value.LocationIds[i] == "@BLACK_MARKET") value.LocationIds[i] = "3003";

            // getting the location, we need to clear out the data before the @
            string? query;
            if (value.LocationIds[i].Contains("@"))
            {
                query = value.LocationIds[i].Split('@')[1];
            }
            else
            {
                query = value.LocationIds[i];
            }
            var location = AlbionLocations.Get(query) ?? AlbionLocations.Unknown;
            var type = Enum.TryParse(typeof(AlbionMailInfoType), value.Types[i], true, out object? parsedType) ? (AlbionMailInfoType)parsedType : AlbionMailInfoType.UNKNOWN;
            AlbionMail mail = new(value.MailIds[i], location.MarketLocation?.IdInt ?? -2, playerState.PlayerName, type, new DateTime(value.Received[i]), playerState.AlbionServer.Id);

            if (mail.Type != AlbionMailInfoType.UNKNOWN)
            {
                AlbionMails.Add(mail);
            }
        }
        await mailService.AddMails(AlbionMails);

        await Task.CompletedTask;
    }
}
