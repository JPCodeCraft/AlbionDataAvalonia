using System.Collections.Generic;

namespace Albion.Network
{
    public class RequestPacket
    {
        public RequestPacket(short operationCode, Dictionary<byte, object> parameters)
            : this(operationCode, parameters, 0, false, 1)
        {
        }

        public RequestPacket(
            short operationCode,
            Dictionary<byte, object> parameters,
            int messageSizeBytes,
            bool isFragmented,
            int fragmentCount)
        {
            OperationCode = operationCode;
            Parameters = parameters;
            MessageSizeBytes = messageSizeBytes;
            IsFragmented = isFragmented;
            FragmentCount = fragmentCount;
        }

        public short OperationCode { get; }
        public Dictionary<byte, object> Parameters { get; }
        public int MessageSizeBytes { get; }
        public bool IsFragmented { get; }
        public int FragmentCount { get; }
    }
}
