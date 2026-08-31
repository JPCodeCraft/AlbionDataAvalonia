namespace PhotonPackageParser
{
    /// <summary>
    /// Describes the legacy packet status and whether the payload contained
    /// traffic that is useful for identifying an active Photon data stream.
    /// </summary>
    public readonly struct PacketReceiveResult
    {
        public PacketReceiveResult(PacketStatus status, bool hasValidPhotonTraffic)
        {
            Status = status;
            HasValidPhotonTraffic = hasValidPhotonTraffic;
        }

        /// <summary>
        /// Gets the packet status using the existing status precedence rules.
        /// </summary>
        public PacketStatus Status { get; }

        /// <summary>
        /// Gets whether at least one application payload, accepted fragment,
        /// or encrypted payload was observed, even if separate coalesced data
        /// made <see cref="Status"/> malformed. Acknowledgement, control, and
        /// disconnect commands alone do not set this value.
        /// </summary>
        public bool HasValidPhotonTraffic { get; }
    }
}
