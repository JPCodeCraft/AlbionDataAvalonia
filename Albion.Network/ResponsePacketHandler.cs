using System;
using System.Threading.Tasks;
using AlbionDataAvalonia.Shared;

namespace Albion.Network
{
    public abstract class ResponsePacketHandler<TOperation> : PacketHandler<ResponsePacket> where TOperation : BaseOperation
    {
        private readonly int operationCode;

        public ResponsePacketHandler(int operationCode)
        {
            this.operationCode = operationCode;
        }

        protected abstract Task OnActionAsync(TOperation value);

        protected internal override Task OnHandleAsync(ResponsePacket packet)
        {
            Console.WriteLine($"ResponsePacketHandler: Received response with code {(OperationCodes)packet.OperationCode}");

            if (operationCode != packet.OperationCode)
            {
                return NextAsync(packet);
            }
            else
            {
                TOperation instance = (TOperation)Activator.CreateInstance(typeof(TOperation), packet.Parameters);

                return OnActionAsync(instance);
            }
        }
    }
}
