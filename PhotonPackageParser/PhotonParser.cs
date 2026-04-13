using Protocol16;
using Protocol16.Photon;
using System;
using System.Collections.Generic;
using System.IO;

namespace PhotonPackageParser
{
    public abstract class PhotonParser
    {
        private const int CommandHeaderLength = 12;
        private const int PhotonHeaderLength = 12;

        private readonly Dictionary<int, SegmentedPackage> _pendingSegments = new Dictionary<int, SegmentedPackage>();

        public PacketStatus ReceivePacket(byte[] payload)
        {
            if (payload.Length < PhotonHeaderLength)
            {
                return PacketStatus.InvalidHeader;
            }

            int offset = 0;
            NumberDeserializer.Deserialize(out short peerId, payload, ref offset);
            ReadByte(out byte flags, payload, ref offset);
            ReadByte(out byte commandCount, payload, ref offset);
            NumberDeserializer.Deserialize(out int timestamp, payload, ref offset);
            NumberDeserializer.Deserialize(out int challenge, payload, ref offset);

            bool isEncrypted = flags == 1;
            bool isCrcEnabled = flags == 0xCC;

            if (isEncrypted)
            {
                // This doesn't really work, flags is always 0?
                return PacketStatus.Encrypted;
            }

            if (isCrcEnabled)
            {
                int ignoredOffset = 0;
                NumberDeserializer.Deserialize(out int crc, payload, ref ignoredOffset);
                NumberSerializer.Serialize(0, payload, ref offset);

                if (crc != CrcCalculator.Calculate(payload, payload.Length))
                {
                    // Invalid crc
                    return PacketStatus.InvalidCrc;
                }
            }

            PacketStatus response = PacketStatus.Undefined;

            for (int commandIdx = 0; commandIdx < commandCount; commandIdx++)
            {
                response = HandleCommand(payload, ref offset);
            }

            return response;
        }


        protected abstract void OnRequest(byte operationCode, Dictionary<byte, object> parameters);

        protected abstract void OnResponse(byte operationCode, short returnCode, string debugMessage, Dictionary<byte, object> parameters);

        protected abstract void OnEvent(byte code, Dictionary<byte, object> parameters);

        private PacketStatus HandleCommand(byte[] source, ref int offset)
        {
            ReadByte(out byte commandType, source, ref offset);
            ReadByte(out byte channelId, source, ref offset);
            ReadByte(out byte commandFlags, source, ref offset);
            // Skip 1 byte
            offset++;
            NumberDeserializer.Deserialize(out int commandLength, source, ref offset);
            NumberDeserializer.Deserialize(out int sequenceNumber, source, ref offset);
            commandLength -= CommandHeaderLength;

            PacketStatus response = PacketStatus.Undefined;

            switch ((CommandType)commandType)
            {
                case CommandType.Disconnect:
                    {
                        return PacketStatus.DisconnectCommand;
                    }
                case CommandType.SendUnreliable:
                    {
                        offset += 4;
                        commandLength -= 4;
                        goto case CommandType.SendReliable;
                    }
                case CommandType.SendReliable:
                    {
                        response = HandleSendReliable(source, ref offset, ref commandLength);
                        break;
                    }
                case CommandType.SendFragment:
                    {
                        response = HandleSendFragment(source, ref offset, ref commandLength);
                        break;
                    }
                default:
                    {
                        offset += commandLength;
                        break;
                    }
            }
            return response;
        }

        private PacketStatus HandleSendReliable(byte[] source, ref int offset, ref int commandLength)
        {
            ReadByte(out byte signalByte, source, ref offset);
            commandLength--;
            ReadByte(out byte messageType, source, ref offset);
            commandLength--;

            int operationLength = commandLength;
            var payload = new Protocol16Stream(operationLength);
            payload.Write(source, offset, operationLength);
            payload.Seek(0L, SeekOrigin.Begin);

            offset += operationLength;

            bool preferProtocol18 = signalByte == 0xF0;

            // Encrypted message for market data?
            if (messageType > 128)
            {
                return PacketStatus.Encrypted;
            }

            switch ((MessageType)messageType)
            {
                case MessageType.OperationRequest:
                case MessageType.InternalOperationRequest:
                    {
                        OperationRequest requestData = DeserializeOperationRequest(payload, preferProtocol18);
                        OnRequest(requestData.OperationCode, requestData.Parameters);
                        break;
                    }
                case MessageType.OperationResponse:
                case MessageType.InternalOperationResponse:
                    {
                        OperationResponse responseData = DeserializeOperationResponse(payload, preferProtocol18);
                        OnResponse(responseData.OperationCode, responseData.ReturnCode, responseData.DebugMessage, responseData.Parameters);
                        break;
                    }
                case MessageType.Event:
                    {
                        EventData eventData = DeserializeEventData(payload, preferProtocol18);
                        OnEvent(eventData.Code, eventData.Parameters);
                        break;
                    }
            }
            return PacketStatus.Success;
        }

        private static OperationRequest DeserializeOperationRequest(Protocol16Stream payload, bool preferProtocol18)
        {
            Exception firstError = new ArgumentException("Unable to deserialize operation request.");

            if (preferProtocol18)
            {
                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeOperationRequest, out OperationRequest protocol18Request, out firstError))
                {
                    return protocol18Request;
                }

                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeOperationRequest, out OperationRequest protocol16Fallback, out _))
                {
                    return protocol16Fallback;
                }
            }
            else
            {
                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeOperationRequest, out OperationRequest protocol16Request, out firstError))
                {
                    return protocol16Request;
                }

                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeOperationRequest, out OperationRequest protocol18Fallback, out _))
                {
                    return protocol18Fallback;
                }
            }

            throw firstError;
        }

        private static OperationResponse DeserializeOperationResponse(Protocol16Stream payload, bool preferProtocol18)
        {
            Exception firstError = new ArgumentException("Unable to deserialize operation response.");

            if (preferProtocol18)
            {
                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeOperationResponse, out OperationResponse protocol18Response, out firstError))
                {
                    return protocol18Response;
                }

                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeOperationResponse, out OperationResponse protocol16Fallback, out _))
                {
                    return protocol16Fallback;
                }
            }
            else
            {
                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeOperationResponse, out OperationResponse protocol16Response, out firstError))
                {
                    return protocol16Response;
                }

                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeOperationResponse, out OperationResponse protocol18Fallback, out _))
                {
                    return protocol18Fallback;
                }
            }

            throw firstError;
        }

        private static EventData DeserializeEventData(Protocol16Stream payload, bool preferProtocol18)
        {
            Exception firstError = new ArgumentException("Unable to deserialize event data.");

            if (preferProtocol18)
            {
                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeEventData, out EventData protocol18Event, out firstError))
                {
                    return protocol18Event;
                }

                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeEventData, out EventData protocol16Fallback, out _))
                {
                    return protocol16Fallback;
                }
            }
            else
            {
                if (TryDeserialize(payload, Protocol16Deserializer.DeserializeEventData, out EventData protocol16Event, out firstError))
                {
                    return protocol16Event;
                }

                if (TryDeserialize(payload, Protocol18Deserializer.DeserializeEventData, out EventData protocol18Fallback, out _))
                {
                    return protocol18Fallback;
                }
            }

            throw firstError;
        }

        private static bool TryDeserialize<T>(Protocol16Stream payload, Func<Protocol16Stream, T> deserializer, out T result, out Exception error)
        {
            long start = payload.Position;
            try
            {
                result = deserializer(payload);
                error = new InvalidOperationException("No deserialization error.");
                return true;
            }
            catch (Exception ex)
            {
                payload.Seek(start, SeekOrigin.Begin);
                result = default!;
                error = ex;
                return false;
            }
        }

        private PacketStatus HandleSendFragment(byte[] source, ref int offset, ref int commandLength)
        {
            NumberDeserializer.Deserialize(out int startSequenceNumber, source, ref offset);
            commandLength -= 4;
            NumberDeserializer.Deserialize(out int fragmentCount, source, ref offset);
            commandLength -= 4;
            NumberDeserializer.Deserialize(out int fragmentNumber, source, ref offset);
            commandLength -= 4;
            NumberDeserializer.Deserialize(out int totalLength, source, ref offset);
            commandLength -= 4;
            NumberDeserializer.Deserialize(out int fragmentOffset, source, ref offset);
            commandLength -= 4;

            int fragmentLength = commandLength;
            return HandleSegmentedPayload(startSequenceNumber, totalLength, fragmentLength, fragmentOffset, source, ref offset);
        }

        private PacketStatus HandleFinishedSegmentedPackage(byte[] totalPayload)
        {
            int offset = 0;
            int commandLength = totalPayload.Length;
            return HandleSendReliable(totalPayload, ref offset, ref commandLength);
        }

        private PacketStatus HandleSegmentedPayload(int startSequenceNumber, int totalLength, int fragmentLength, int fragmentOffset, byte[] source, ref int offset)
        {
            SegmentedPackage segmentedPackage = GetSegmentedPackage(startSequenceNumber, totalLength);

            Buffer.BlockCopy(source, offset, segmentedPackage.TotalPayload, fragmentOffset, fragmentLength);
            offset += fragmentLength;
            segmentedPackage.BytesWritten += fragmentLength;

            if (segmentedPackage.BytesWritten >= segmentedPackage.TotalLength)
            {
                _pendingSegments.Remove(startSequenceNumber);
                return HandleFinishedSegmentedPackage(segmentedPackage.TotalPayload);
            }

            return PacketStatus.Success;
        }

        private SegmentedPackage GetSegmentedPackage(int startSequenceNumber, int totalLength)
        {
            if (_pendingSegments.TryGetValue(startSequenceNumber, out SegmentedPackage segmentedPackage))
            {
                return segmentedPackage;
            }

            segmentedPackage = new SegmentedPackage
            {
                TotalLength = totalLength,
                TotalPayload = new byte[totalLength],
            };
            _pendingSegments.Add(startSequenceNumber, segmentedPackage);

            return segmentedPackage;
        }

        private static void ReadByte(out byte value, byte[] source, ref int offset)
        {
            value = source[offset++];
        }
    }
}
