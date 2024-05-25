using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class ReadMailResponseHandler : ResponsePacketHandler<ReadMailResponse>
{
    private readonly PlayerState playerState;
    private readonly MailService mailService;
    public ReadMailResponseHandler(PlayerState playerState, MailService mailService) : base((int)OperationCodes.ReadMail)
    {
        this.playerState = playerState;
        this.mailService = mailService;
    }

    protected override async Task OnActionAsync(ReadMailResponse value)
    {
        mailService.AddMailData(value.MailId, value.MailString);

        await Task.CompletedTask;
    }
}
