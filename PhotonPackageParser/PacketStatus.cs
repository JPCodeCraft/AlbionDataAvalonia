namespace PhotonPackageParser
{
    public enum PacketStatus
    {
        Undefined,
        Success,
        Encrypted,
        InvalidCrc,
        InvalidHeader,
        DisconnectCommand
    }
}
