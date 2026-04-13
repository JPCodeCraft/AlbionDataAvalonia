using Protocol16;
using Protocol16.Photon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

        protected virtual void OnRequestDecoded(byte signalByte, byte messageType, byte operationCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
        }

        protected virtual void OnResponseDecoded(byte signalByte, byte messageType, byte operationCode, short returnCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
        }

        protected virtual void OnEventDecoded(byte signalByte, byte messageType, byte eventCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
        }

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
            int payloadOffset = offset;
            var payload = new Protocol16Stream(operationLength);
            payload.Write(source, offset, operationLength);
            payload.Seek(0L, SeekOrigin.Begin);

            offset += operationLength;
            string payloadPreview = GetHexPreview(source, payloadOffset, operationLength);

            // Encrypted message for market data?
            if (messageType > 128)
            {
                return PacketStatus.Encrypted;
            }

            switch ((MessageType)messageType)
            {
                case MessageType.OperationRequest:
                    {
                        try
                        {
                            OperationRequest requestData = Protocol18Deserializer.DeserializeOperationRequest(payload);
                            OnRequestDecoded(signalByte, messageType, requestData.OperationCode, requestData.Parameters, payloadPreview);
                            OnRequest(requestData.OperationCode, requestData.Parameters);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Protocol18 request decode failed. signal=0x{signalByte:X2} messageType={messageType} payloadPreview=\"{payloadPreview}\"", ex);
                        }
                        break;
                    }
                case MessageType.OperationResponse:
                    {
                        try
                        {
                            OperationResponse responseData = Protocol18Deserializer.DeserializeOperationResponse(payload);
                            OnResponseDecoded(signalByte, messageType, responseData.OperationCode, responseData.ReturnCode, responseData.Parameters, payloadPreview);
                            OnResponse(responseData.OperationCode, responseData.ReturnCode, responseData.DebugMessage, responseData.Parameters);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Protocol18 response decode failed. signal=0x{signalByte:X2} messageType={messageType} payloadPreview=\"{payloadPreview}\"", ex);
                        }
                        break;
                    }
                case MessageType.Event:
                    {
                        try
                        {
                            EventData eventData = Protocol18Deserializer.DeserializeEventData(payload);
                            OnEventDecoded(signalByte, messageType, eventData.Code, eventData.Parameters, payloadPreview);
                            OnEvent(eventData.Code, eventData.Parameters);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Protocol18 event decode failed. signal=0x{signalByte:X2} messageType={messageType} payloadPreview=\"{payloadPreview}\"", ex);
                        }
                        break;
                    }
            }
            return PacketStatus.Success;
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

        private static string GetHexPreview(byte[] source, int offset, int count, int maxBytes = 24)
        {
            int previewCount = Math.Min(count, maxBytes);
            if (previewCount <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(previewCount * 3);
            for (int i = 0; i < previewCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(source[offset + i].ToString("X2"));
            }

            if (count > previewCount)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }
    }
}
