using System.Collections.Generic;

namespace Albion.Network
{
    public class ResponsePacket
    {
        public ResponsePacket(short operationCode, Dictionary<byte, object> parameters)
            : this(operationCode, parameters, 0, false, 1)
        {
        }

        public ResponsePacket(
            short operationCode,
            Dictionary<byte, object> parameters,
            int messageSizeBytes,
            bool isFragmented,
            int fragmentCount,
            short returnCode = 0)
        {
            OperationCode = operationCode;
            Parameters = parameters;
            MessageSizeBytes = messageSizeBytes;
            IsFragmented = isFragmented;
            FragmentCount = fragmentCount;
            ReturnCode = returnCode;
        }

        public short OperationCode { get; }
        public short ReturnCode { get; }
        public Dictionary<byte, object> Parameters { get; }
        public int MessageSizeBytes { get; }
        public bool IsFragmented { get; }
        public int FragmentCount { get; }
    }
}
