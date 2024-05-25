using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
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
        value.AlbionMails.ForEach(mail => mail.AlbionServerId = playerState.AlbionServer?.Id ?? 0);
        value.AlbionMails.ForEach(mail => mailService.AddMail(mail));

        await Task.CompletedTask;
    }
}
