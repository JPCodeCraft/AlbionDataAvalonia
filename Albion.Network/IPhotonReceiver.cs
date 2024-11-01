using PhotonPackageParser;

namespace Albion.Network
{
    public interface IPhotonReceiver
    {
        PacketStatus ReceivePacket(byte[] payload);
    }
}
