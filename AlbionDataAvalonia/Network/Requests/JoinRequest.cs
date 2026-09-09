using Albion.Network;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class JoinRequest(Dictionary<byte, object> parameters) : BaseOperation(parameters);
