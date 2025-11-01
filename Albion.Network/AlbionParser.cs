using AlbionDataAvalonia.Shared;
using PhotonPackageParser;
using System;
using System.Collections.Generic;

namespace Albion.Network
{
    internal sealed class AlbionParser : PhotonParser, IPhotonReceiver
    {
        private readonly HandlersCollection handlers;

        public AlbionParser()
        {
            handlers = new HandlersCollection();
        }

        public void AddHandler<TPacket>(PacketHandler<TPacket> handler)
        {
            handlers.Add(handler);
        }

        protected override void OnEvent(byte Code, Dictionary<byte, object> Parameters)
        {
            if (Code == 3)
            {
                Parameters.Add(252, (short)EventCodes.Move);
            }

            short eventCode = ParseEventCode(Parameters);
            var eventPacket = new EventPacket(eventCode, Parameters);

            _ = handlers.HandleAsync(eventPacket);
        }

        protected override void OnRequest(byte OperationCode, Dictionary<byte, object> Parameters)
        {
            short operationCode = ParseOperationCode(Parameters);
            var requestPacket = new RequestPacket(operationCode, Parameters);

            _ = handlers.HandleAsync(requestPacket);
        }

        protected override void OnResponse(byte OperationCode, short ReturnCode, string DebugMessage, Dictionary<byte, object> Parameters)
        {
            short operationCode = ParseOperationCode(Parameters);
            var responsePacket = new ResponsePacket(operationCode, Parameters);

            _ = handlers.HandleAsync(responsePacket);
        }

        private short ParseOperationCode(Dictionary<byte, object> parameters)
        {
            if (!parameters.TryGetValue(253, out object value))
            {
                throw new InvalidOperationException();
            }

            switch (value)
            {
                case short shortValue:
                    return shortValue;
                case int intValue:
                    return (short)intValue;
                case byte byteValue:
                    return byteValue;
                case Enum enumValue:
                    return (short)Convert.ToInt16(enumValue);
                default:
                    throw new InvalidCastException($"Unable to cast object of type '{value.GetType()}' to Int16.");
            }
        }

        private short ParseEventCode(Dictionary<byte, object> parameters)
        {
            if (!parameters.TryGetValue(252, out object value))
            {
                throw new InvalidOperationException();
            }

            switch (value)
            {
                case short shortValue:
                    return shortValue;
                case int intValue:
                    return (short)intValue;
                case byte byteValue:
                    return byteValue;
                case Enum enumValue:
                    return (short)Convert.ToInt16(enumValue);
                default:
                    throw new InvalidCastException($"Unable to cast object of type '{value.GetType()}' to Int16.");
            }
        }
    }
}
