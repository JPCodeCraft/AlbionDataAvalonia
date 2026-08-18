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
        private const int MaxSegmentedPayloadLength = 16 * 1024 * 1024;
        private const int MaxPendingSegmentCount = 128;
        private const int MaxFragmentsPerPayload = 32 * 1024;
        private const long MaxPendingSegmentBytes = 64L * 1024 * 1024;
        private static readonly TimeSpan PendingSegmentLifetime = TimeSpan.FromSeconds(30);

        private readonly Dictionary<SegmentedPackageKey, SegmentedPackage> _pendingSegments =
            new Dictionary<SegmentedPackageKey, SegmentedPackage>();
        private readonly object _receiveLock = new object();
        private long _pendingSegmentBytes;

        public PacketStatus ReceivePacket(byte[] payload)
        {
            lock (_receiveLock)
            {
                RemoveExpiredSegments(DateTime.UtcNow);
                return ReceivePacketCore(payload);
            }
        }

        private PacketStatus ReceivePacketCore(byte[] payload)
        {
            if (payload.Length < PhotonHeaderLength)
            {
                return PacketStatus.InvalidHeader;
            }

            if (!TryGetPhotonPacketIdentity(
                payload,
                packetOffset: 0,
                out short peerId,
                out int challenge) ||
                !TryGetPhotonPacketLength(
                    payload,
                    packetOffset: 0,
                    out int firstPacketLength,
                    out bool isTerminalEncryptedPacket))
            {
                return PacketStatus.InvalidHeader;
            }

            if (isTerminalEncryptedPacket || firstPacketLength == payload.Length)
            {
                return ReceiveSinglePacket(payload);
            }

            var packetRanges = new List<PhotonPacketRange>
            {
                new PhotonPacketRange(offset: 0, length: firstPacketLength),
            };
            int packetOffset = firstPacketLength;
            PacketStatus framingStatus = PacketStatus.Undefined;

            while (packetOffset < payload.Length)
            {
                if (!HasMatchingPhotonPacketIdentity(
                    payload,
                    packetOffset,
                    peerId,
                    challenge) ||
                    !TryGetPhotonPacketLength(
                        payload,
                        packetOffset,
                        out int packetLength,
                        out isTerminalEncryptedPacket))
                {
                    framingStatus = PacketStatus.InvalidHeader;
                    OnTrailingPayloadRejected(
                        payload.Length,
                        packetOffset,
                        payload.Length - packetOffset);
                    break;
                }

                packetRanges.Add(new PhotonPacketRange(packetOffset, packetLength));
                packetOffset += packetLength;

                if (isTerminalEncryptedPacket)
                {
                    break;
                }
            }

            if (packetRanges.Count > 1)
            {
                var packetSizes = new List<int>(packetRanges.Count);
                foreach (PhotonPacketRange packetRange in packetRanges)
                {
                    packetSizes.Add(packetRange.Length);
                }

                OnCoalescedPayloadDetected(payload.Length, packetSizes);
            }

            PacketStatus response = framingStatus;
            foreach (PhotonPacketRange packetRange in packetRanges)
            {
                var packetPayload = new byte[packetRange.Length];
                Buffer.BlockCopy(
                    payload,
                    packetRange.Offset,
                    packetPayload,
                    0,
                    packetRange.Length);

                response = CombinePacketStatus(
                    response,
                    ReceiveSinglePacket(packetPayload));
            }

            return response;
        }

        private PacketStatus ReceiveSinglePacket(byte[] payload)
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
                if (!HasAvailable(payload, offset, sizeof(int)))
                {
                    return PacketStatus.InvalidHeader;
                }

                NumberDeserializer.Deserialize(out int crc, payload, ref offset);
                var crcPayload = (byte[])payload.Clone();
                Array.Clear(crcPayload, PhotonHeaderLength, sizeof(int));

                if (unchecked((uint)crc) != CrcCalculator.Calculate(crcPayload, crcPayload.Length))
                {
                    return PacketStatus.InvalidCrc;
                }
            }

            PacketStatus response = PacketStatus.Undefined;

            for (int commandIdx = 0; commandIdx < commandCount; commandIdx++)
            {
                if (!HasAvailable(payload, offset, CommandHeaderLength))
                {
                    return PacketStatus.InvalidHeader;
                }

                PacketStatus commandStatus = HandleCommand(
                    payload,
                    ref offset,
                    peerId,
                    challenge);
                if (commandStatus == PacketStatus.InvalidHeader)
                {
                    return CombinePacketStatus(response, commandStatus);
                }

                response = CombinePacketStatus(response, commandStatus);
            }

            return offset == payload.Length
                ? response
                : CombinePacketStatus(response, PacketStatus.InvalidHeader);
        }


        protected int CurrentMessageSizeBytes { get; private set; }

        protected bool CurrentMessageIsFragmented { get; private set; }

        protected int CurrentMessageFragmentCount { get; private set; }

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

        protected virtual void OnSegmentedPayloadExpired(
            short peerId,
            int challenge,
            byte channelId,
            int startSequenceNumber,
            int totalMessageBytes,
            int expectedFragmentCount,
            int receivedFragmentCount,
            long receivedBytes,
            IReadOnlyList<int> missingFragmentNumbers,
            TimeSpan age,
            TimeSpan idleTime)
        {
        }

        protected virtual void OnCoalescedPayloadDetected(
            int capturedPayloadBytes,
            IReadOnlyList<int> photonPacketSizes)
        {
        }

        protected virtual void OnTrailingPayloadRejected(
            int capturedPayloadBytes,
            int consumedPayloadBytes,
            int remainingPayloadBytes)
        {
        }

        private PacketStatus HandleCommand(
            byte[] source,
            ref int offset,
            short peerId,
            int challenge)
        {
            if (!HasAvailable(source, offset, CommandHeaderLength))
            {
                return PacketStatus.InvalidHeader;
            }

            ReadByte(out byte commandType, source, ref offset);
            ReadByte(out byte channelId, source, ref offset);
            ReadByte(out byte commandFlags, source, ref offset);
            // Skip 1 byte
            offset++;
            NumberDeserializer.Deserialize(out int commandLength, source, ref offset);
            NumberDeserializer.Deserialize(out int sequenceNumber, source, ref offset);
            commandLength -= CommandHeaderLength;

            if (commandLength < 0 || !HasAvailable(source, offset, commandLength))
            {
                return PacketStatus.InvalidHeader;
            }

            PacketStatus response = PacketStatus.Undefined;

            switch ((CommandType)commandType)
            {
                case CommandType.Disconnect:
                    {
                        offset += commandLength;
                        return PacketStatus.DisconnectCommand;
                    }
                case CommandType.SendUnreliable:
                    {
                        if (commandLength < 4 || !HasAvailable(source, offset, 4))
                        {
                            return PacketStatus.InvalidHeader;
                        }

                        offset += 4;
                        commandLength -= 4;
                        goto case CommandType.SendReliable;
                    }
                case CommandType.SendReliable:
                    {
                        response = HandleSendReliable(
                            source,
                            ref offset,
                            ref commandLength,
                            isFragmented: false,
                            fragmentCount: 1);
                        break;
                    }
                case CommandType.SendFragment:
                    {
                        response = HandleSendFragment(
                            source,
                            ref offset,
                            ref commandLength,
                            peerId,
                            challenge,
                            channelId);
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

        private PacketStatus HandleSendReliable(
            byte[] source,
            ref int offset,
            ref int commandLength,
            bool isFragmented,
            int fragmentCount)
        {
            if (commandLength < 2 || !HasAvailable(source, offset, commandLength))
            {
                return PacketStatus.InvalidHeader;
            }

            int messageSizeBytes = commandLength;
            CurrentMessageSizeBytes = messageSizeBytes;
            CurrentMessageIsFragmented = isFragmented;
            CurrentMessageFragmentCount = fragmentCount;

            ReadByte(out byte signalByte, source, ref offset);
            commandLength--;
            ReadByte(out byte messageType, source, ref offset);
            commandLength--;

            int operationLength = commandLength;
            int payloadOffset = offset;
            var payload = new MemoryStream(source, offset, operationLength, writable: false);

            offset += operationLength;
            string payloadPreview = GetHexPreview(source, payloadOffset, operationLength);

            // Encrypted message for market data?
            if (messageType == 131)
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

        private PacketStatus HandleSendFragment(
            byte[] source,
            ref int offset,
            ref int commandLength,
            short peerId,
            int challenge,
            byte channelId)
        {
            const int fragmentHeaderLength = 20;
            if (commandLength < fragmentHeaderLength || !HasAvailable(source, offset, fragmentHeaderLength))
            {
                return PacketStatus.InvalidHeader;
            }

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
            if (fragmentLength < 0 || !HasAvailable(source, offset, fragmentLength))
            {
                return PacketStatus.InvalidHeader;
            }

            if (fragmentCount <= 0 ||
                fragmentCount > MaxFragmentsPerPayload ||
                fragmentNumber < 0 ||
                fragmentNumber >= fragmentCount ||
                totalLength <= 0 ||
                totalLength > MaxSegmentedPayloadLength ||
                fragmentCount > totalLength ||
                fragmentLength <= 0 ||
                fragmentOffset < 0 ||
                fragmentLength > totalLength ||
                fragmentOffset > totalLength - fragmentLength)
            {
                return PacketStatus.InvalidHeader;
            }

            return HandleSegmentedPayload(
                new SegmentedPackageKey(
                    peerId,
                    challenge,
                    channelId,
                    startSequenceNumber),
                fragmentCount,
                fragmentNumber,
                totalLength,
                fragmentLength,
                fragmentOffset,
                source,
                ref offset);
        }

        private PacketStatus HandleFinishedSegmentedPackage(byte[] totalPayload, int fragmentCount)
        {
            int offset = 0;
            int commandLength = totalPayload.Length;
            return HandleSendReliable(
                totalPayload,
                ref offset,
                ref commandLength,
                isFragmented: true,
                fragmentCount);
        }

        private PacketStatus HandleSegmentedPayload(
            SegmentedPackageKey segmentKey,
            int fragmentCount,
            int fragmentNumber,
            int totalLength,
            int fragmentLength,
            int fragmentOffset,
            byte[] source,
            ref int offset)
        {
            DateTime now = DateTime.UtcNow;
            SegmentedPackage? segmentedPackage = GetSegmentedPackage(
                segmentKey,
                totalLength,
                fragmentCount,
                now);

            if (segmentedPackage == null)
            {
                return PacketStatus.InvalidHeader;
            }

            if (segmentedPackage.ReceivedFragments.TryGetValue(fragmentNumber, out FragmentRange receivedFragment))
            {
                bool matchesExistingFragment =
                    receivedFragment.Offset == fragmentOffset &&
                    receivedFragment.Length == fragmentLength &&
                    PayloadMatches(
                        source,
                        offset,
                        segmentedPackage.TotalPayload,
                        fragmentOffset,
                        fragmentLength);

                offset += fragmentLength;
                if (!matchesExistingFragment)
                {
                    return PacketStatus.InvalidHeader;
                }

                segmentedPackage.LastUpdatedUtc = now;
                return PacketStatus.Success;
            }

            Buffer.BlockCopy(source, offset, segmentedPackage.TotalPayload, fragmentOffset, fragmentLength);
            offset += fragmentLength;
            segmentedPackage.ReceivedFragments.Add(
                fragmentNumber,
                new FragmentRange(fragmentOffset, fragmentLength));
            segmentedPackage.BytesWritten += fragmentLength;
            segmentedPackage.LastUpdatedUtc = now;

            if (segmentedPackage.ReceivedFragments.Count == segmentedPackage.FragmentCount)
            {
                if (segmentedPackage.BytesWritten != segmentedPackage.TotalLength ||
                    !HasContiguousCoverage(
                        segmentedPackage.ReceivedFragments.Values,
                        segmentedPackage.TotalLength))
                {
                    RemovePendingSegment(segmentKey);
                    return PacketStatus.InvalidHeader;
                }

                byte[] totalPayload = segmentedPackage.TotalPayload;
                RemovePendingSegment(segmentKey);
                return HandleFinishedSegmentedPackage(totalPayload, segmentedPackage.FragmentCount);
            }

            if (segmentedPackage.BytesWritten >= segmentedPackage.TotalLength)
            {
                RemovePendingSegment(segmentKey);
                return PacketStatus.InvalidHeader;
            }

            return PacketStatus.Success;
        }

        private SegmentedPackage? GetSegmentedPackage(
            SegmentedPackageKey segmentKey,
            int totalLength,
            int fragmentCount,
            DateTime now)
        {
            if (_pendingSegments.TryGetValue(segmentKey, out SegmentedPackage segmentedPackage))
            {
                if (segmentedPackage.TotalLength != totalLength ||
                    segmentedPackage.FragmentCount != fragmentCount)
                {
                    return null;
                }

                return segmentedPackage;
            }

            if (_pendingSegments.Count >= MaxPendingSegmentCount ||
                _pendingSegmentBytes > MaxPendingSegmentBytes - totalLength)
            {
                return null;
            }

            segmentedPackage = new SegmentedPackage
            {
                TotalLength = totalLength,
                FragmentCount = fragmentCount,
                TotalPayload = new byte[totalLength],
                CreatedUtc = now,
                LastUpdatedUtc = now,
            };
            _pendingSegments.Add(segmentKey, segmentedPackage);
            _pendingSegmentBytes += totalLength;

            return segmentedPackage;
        }

        private void RemoveExpiredSegments(DateTime now)
        {
            List<SegmentedPackageKey>? expiredSegmentKeys = null;

            foreach (KeyValuePair<SegmentedPackageKey, SegmentedPackage> entry in _pendingSegments)
            {
                if (now - entry.Value.LastUpdatedUtc <= PendingSegmentLifetime)
                {
                    continue;
                }

                if (expiredSegmentKeys == null)
                {
                    expiredSegmentKeys = new List<SegmentedPackageKey>();
                }

                expiredSegmentKeys.Add(entry.Key);
            }

            if (expiredSegmentKeys == null)
            {
                return;
            }

            foreach (SegmentedPackageKey segmentKey in expiredSegmentKeys)
            {
                if (_pendingSegments.TryGetValue(segmentKey, out SegmentedPackage segmentedPackage))
                {
                    OnSegmentedPayloadExpired(
                        segmentKey.PeerId,
                        segmentKey.Challenge,
                        segmentKey.ChannelId,
                        segmentKey.StartSequenceNumber,
                        segmentedPackage.TotalLength,
                        segmentedPackage.FragmentCount,
                        segmentedPackage.ReceivedFragments.Count,
                        segmentedPackage.BytesWritten,
                        GetMissingFragmentNumbers(segmentedPackage),
                        now - segmentedPackage.CreatedUtc,
                        now - segmentedPackage.LastUpdatedUtc);
                }

                RemovePendingSegment(segmentKey);
            }
        }

        private static IReadOnlyList<int> GetMissingFragmentNumbers(SegmentedPackage segmentedPackage)
        {
            var missingFragmentNumbers = new List<int>();

            for (int fragmentNumber = 0; fragmentNumber < segmentedPackage.FragmentCount; fragmentNumber++)
            {
                if (!segmentedPackage.ReceivedFragments.ContainsKey(fragmentNumber))
                {
                    missingFragmentNumbers.Add(fragmentNumber);
                }
            }

            return missingFragmentNumbers;
        }

        private void RemovePendingSegment(SegmentedPackageKey segmentKey)
        {
            if (!_pendingSegments.TryGetValue(segmentKey, out SegmentedPackage segmentedPackage))
            {
                return;
            }

            _pendingSegments.Remove(segmentKey);
            _pendingSegmentBytes -= segmentedPackage.TotalLength;
        }

        private static bool HasContiguousCoverage(
            IEnumerable<FragmentRange> fragments,
            int totalLength)
        {
            var orderedFragments = new List<FragmentRange>(fragments);
            orderedFragments.Sort((left, right) => left.Offset.CompareTo(right.Offset));

            var expectedOffset = 0;
            foreach (FragmentRange fragment in orderedFragments)
            {
                if (fragment.Offset != expectedOffset)
                {
                    return false;
                }

                expectedOffset += fragment.Length;
            }

            return expectedOffset == totalLength;
        }

        private static bool PayloadMatches(
            byte[] source,
            int sourceOffset,
            byte[] destination,
            int destinationOffset,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                if (source[sourceOffset + index] != destination[destinationOffset + index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetPhotonPacketIdentity(
            byte[] payload,
            int packetOffset,
            out short peerId,
            out int challenge)
        {
            peerId = default;
            challenge = default;

            if (!HasAvailable(payload, packetOffset, PhotonHeaderLength))
            {
                return false;
            }

            int peerIdOffset = packetOffset;
            NumberDeserializer.Deserialize(out peerId, payload, ref peerIdOffset);

            int challengeOffset = packetOffset + 8;
            NumberDeserializer.Deserialize(out challenge, payload, ref challengeOffset);
            return true;
        }

        private static bool HasMatchingPhotonPacketIdentity(
            byte[] payload,
            int packetOffset,
            short expectedPeerId,
            int expectedChallenge)
        {
            return TryGetPhotonPacketIdentity(
                payload,
                packetOffset,
                out short peerId,
                out int challenge) &&
                peerId == expectedPeerId &&
                challenge == expectedChallenge;
        }

        private static bool TryGetPhotonPacketLength(
            byte[] payload,
            int packetOffset,
            out int packetLength,
            out bool isTerminalEncryptedPacket)
        {
            packetLength = 0;
            isTerminalEncryptedPacket = false;
            if (!HasAvailable(payload, packetOffset, PhotonHeaderLength))
            {
                return false;
            }

            byte flags = payload[packetOffset + 2];
            if (flags == 1)
            {
                packetLength = payload.Length - packetOffset;
                isTerminalEncryptedPacket = true;
                return true;
            }

            int packetHeaderLength = flags == 0xCC
                ? PhotonHeaderLength + sizeof(int)
                : PhotonHeaderLength;
            if (!HasAvailable(payload, packetOffset, packetHeaderLength))
            {
                return false;
            }

            byte commandCount = payload[packetOffset + 3];
            if (commandCount == 0)
            {
                return false;
            }

            int commandOffset = packetOffset + packetHeaderLength;

            for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                if (!HasAvailable(payload, commandOffset, CommandHeaderLength))
                {
                    return false;
                }

                int commandLengthOffset = commandOffset + 4;
                NumberDeserializer.Deserialize(
                    out int commandLength,
                    payload,
                    ref commandLengthOffset);

                if (commandLength < CommandHeaderLength ||
                    !HasAvailable(payload, commandOffset, commandLength))
                {
                    return false;
                }

                commandOffset += commandLength;
            }

            packetLength = commandOffset - packetOffset;
            return packetLength >= packetHeaderLength;
        }

        private static PacketStatus CombinePacketStatus(
            PacketStatus current,
            PacketStatus candidate)
        {
            return GetPacketStatusPriority(candidate) > GetPacketStatusPriority(current)
                ? candidate
                : current;
        }

        private static int GetPacketStatusPriority(PacketStatus status)
        {
            switch (status)
            {
                case PacketStatus.Encrypted:
                    return 5;
                case PacketStatus.InvalidCrc:
                    return 4;
                case PacketStatus.InvalidHeader:
                    return 3;
                case PacketStatus.DisconnectCommand:
                    return 2;
                case PacketStatus.Success:
                    return 1;
                default:
                    return 0;
            }
        }

        private static void ReadByte(out byte value, byte[] source, ref int offset)
        {
            if (!HasAvailable(source, offset, 1))
            {
                throw new ArgumentException("Unexpected end of packet while reading byte.");
            }

            value = source[offset++];
        }

        private static bool HasAvailable(byte[] source, int offset, int count)
        {
            return count >= 0 && offset >= 0 && source.Length - offset >= count;
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

        private readonly struct PhotonPacketRange
        {
            public PhotonPacketRange(int offset, int length)
            {
                Offset = offset;
                Length = length;
            }

            public int Offset { get; }

            public int Length { get; }
        }
    }
}
