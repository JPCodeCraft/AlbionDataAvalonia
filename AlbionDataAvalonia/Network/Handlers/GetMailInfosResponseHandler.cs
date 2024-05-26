using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class GetMailInfosResponseHandler : ResponsePacketHandler<GetMailInfosResponse>
{
    private readonly PlayerState playerState;
    private readonly MailService mailService;
    private readonly SettingsManager settingsManager;
    public GetMailInfosResponseHandler(PlayerState playerState, MailService mailService, SettingsManager settingsManager) : base((int)OperationCodes.GetMailInfos)
    {
        this.playerState = playerState;
        this.mailService = mailService;
        this.settingsManager = settingsManager;
    }

    protected override async Task OnActionAsync(GetMailInfosResponse value)
    {
        List<AlbionMail> AlbionMails = new();

        if (playerState.AlbionServer == null)
        {
            return;
        }

        for (int i = 0; i < value.MailIds.Length; i++)
        {
            var location = AlbionLocations.TryParse(value.LocationIds[i], out var loc) ? loc ?? AlbionLocations.Unknown : AlbionLocations.Unknown;
            var type = Enum.TryParse(typeof(MailInfoType), value.Types[i], true, out object? parsedType) ? (MailInfoType)parsedType : MailInfoType.UNKNOWN;
            AlbionMail mail = new(value.MailIds[i], location.Id, type, new DateTime(value.Received[i]), playerState.AlbionServer.Id, settingsManager.UserSettings.SalesTax);

            if (mail.Type != MailInfoType.UNKNOWN)
            {
                AlbionMails.Add(mail);
            }
        }
        await mailService.AddMails(AlbionMails);

        await Task.CompletedTask;
    }
}
