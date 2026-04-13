using AlbionDataAvalonia.Shared;
using PhotonPackageParser;
using System;
using System.Collections.Generic;
#if DEBUG
using Serilog;
using System.Collections;
using System.Linq;
#endif

namespace Albion.Network
{
    internal sealed class AlbionParser : PhotonParser, IPhotonReceiver
    {
        private readonly HandlersCollection handlers;
#if DEBUG
        // Debug packet logging settings.
        private const bool EnableParserDebugPacketLogging = false;
        private const bool EnableProtocol18CodeDebugLogging = false;
        // Toggle this deep scan on/off as needed. It is expensive on busy streams.
        private const bool EnableDeepParameterValueFilter = false;
        private const bool EnableNoiseCodeFilter = true;
        private static readonly string[] RequiredParameterTexts = new[] { "" };
        private static readonly EventCodes[] NoiseFilteredEventCodes = new[] { EventCodes.Move };
        private static readonly OperationCodes[] NoiseFilteredOperationCodes = new[] { OperationCodes.Move };
#endif

        public AlbionParser()
        {
            handlers = new HandlersCollection();
        }

        public void AddHandler<TPacket>(PacketHandler<TPacket> handler)
        {
            handlers.Add(handler);
        }

#if DEBUG
        protected override void OnRequestDecoded(byte signalByte, byte messageType, byte operationCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
            if (!EnableProtocol18CodeDebugLogging)
            {
                return;
            }

            Log.Debug(
                "Photon decoded request signal=0x{SignalByte:X2} messageType={MessageType} payloadPreview={PayloadPreview} keys=[{ParameterKeys}] param253={Param253} primary={Primary}",
                signalByte,
                messageType,
                payloadPreview,
                DescribeParameterKeys(parameters),
                DescribeCodeParameter(parameters, 253),
                DescribePrimaryParameter(parameters));
        }

        protected override void OnResponseDecoded(byte signalByte, byte messageType, byte operationCode, short returnCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
            if (!EnableProtocol18CodeDebugLogging)
            {
                return;
            }

            Log.Debug(
                "Photon decoded response signal=0x{SignalByte:X2} messageType={MessageType} returnCode={ReturnCode} payloadPreview={PayloadPreview} keys=[{ParameterKeys}] param253={Param253} primary={Primary}",
                signalByte,
                messageType,
                returnCode,
                payloadPreview,
                DescribeParameterKeys(parameters),
                DescribeCodeParameter(parameters, 253),
                DescribePrimaryParameter(parameters));
        }

        protected override void OnEventDecoded(byte signalByte, byte messageType, byte eventCode, Dictionary<byte, object> parameters, string payloadPreview)
        {
            if (!EnableProtocol18CodeDebugLogging)
            {
                return;
            }

            Log.Debug(
                "Photon decoded event signal=0x{SignalByte:X2} messageType={MessageType} payloadPreview={PayloadPreview} keys=[{ParameterKeys}] param252={Param252} primary={Primary}",
                signalByte,
                messageType,
                payloadPreview,
                DescribeParameterKeys(parameters),
                DescribeCodeParameter(parameters, 252),
                DescribePrimaryParameter(parameters));
        }

#endif

        protected override void OnEvent(byte Code, Dictionary<byte, object> Parameters)
        {
            if (Code == 3)
            {
                Parameters[252] = (short)EventCodes.Move;
            }

            if (!TryParseEventCode(Parameters, out short eventCode))
            {
#if DEBUG
                if (EnableProtocol18CodeDebugLogging)
                {
                    Log.Debug(
                        "Skipping unroutable event packet. keys=[{ParameterKeys}] param252={Param252} primary={Primary}",
                        DescribeParameterKeys(Parameters),
                        DescribeCodeParameter(Parameters, 252),
                        DescribePrimaryParameter(Parameters));
                }
#endif
                return;
            }
#if DEBUG
            if (EnableParserDebugPacketLogging && TryGetEventLogMatch(eventCode, Parameters, out string eventMatchPath, out object? eventMatchValue, out string? eventMatchedText))
            {
                Log.Verbose(
                    "Received event packet {PacketCode} ({PacketName}) with {ParameterCount} parameter(s). MatchPath={MatchPath} MatchText={MatchText} MatchValue={@MatchValue}",
                    eventCode,
                    GetEventName(eventCode),
                    Parameters?.Count ?? 0,
                    eventMatchPath,
                    eventMatchedText,
                    eventMatchValue);
            }
#endif
            var eventPacket = new EventPacket(eventCode, Parameters);

            _ = handlers.HandleAsync(eventPacket);
        }

        protected override void OnRequest(byte OperationCode, Dictionary<byte, object> Parameters)
        {
            short operationCode = ParseOperationCode(Parameters);
#if DEBUG
            if (EnableParserDebugPacketLogging && TryGetOperationLogMatch(operationCode, Parameters, out string requestMatchPath, out object? requestMatchValue, out string? requestMatchedText))
            {
                Log.Verbose(
                    "Received request packet {PacketCode} ({PacketName}) with {ParameterCount} parameter(s). MatchPath={MatchPath} MatchText={MatchText} MatchValue={@MatchValue}",
                    operationCode,
                    GetOperationName(operationCode),
                    Parameters?.Count ?? 0,
                    requestMatchPath,
                    requestMatchedText,
                    requestMatchValue);
            }
#endif
            var requestPacket = new RequestPacket(operationCode, Parameters);

            _ = handlers.HandleAsync(requestPacket);
        }

        protected override void OnResponse(byte OperationCode, short ReturnCode, string DebugMessage, Dictionary<byte, object> Parameters)
        {
            short operationCode = ParseOperationCode(Parameters);
#if DEBUG
            if (EnableParserDebugPacketLogging && TryGetOperationLogMatch(operationCode, Parameters, out string responseMatchPath, out object? responseMatchValue, out string? responseMatchedText))
            {
                Log.Verbose(
                    "Received response packet {PacketCode} ({PacketName}) with {ReturnCode} return code and {ParameterCount} parameter(s). MatchPath={MatchPath} MatchText={MatchText} MatchValue={@MatchValue}",
                    operationCode,
                    GetOperationName(operationCode),
                    ReturnCode,
                    Parameters?.Count ?? 0,
                    responseMatchPath,
                    responseMatchedText,
                    responseMatchValue);
            }
#endif
            var responsePacket = new ResponsePacket(operationCode, Parameters);

            _ = handlers.HandleAsync(responsePacket);
        }

#if DEBUG
        private static bool TryGetEventLogMatch(short eventCode, Dictionary<byte, object> parameters, out string matchPath, out object? matchValue, out string? matchedText)
        {
            if (EnableNoiseCodeFilter && IsNoiseFilteredEventCode(eventCode))
            {
                matchPath = string.Empty;
                matchValue = null;
                matchedText = null;
                return false;
            }

            return TryGetLogMatch(parameters, out matchPath, out matchValue, out matchedText);
        }

        private static bool TryGetOperationLogMatch(short operationCode, Dictionary<byte, object> parameters, out string matchPath, out object? matchValue, out string? matchedText)
        {
            if (EnableNoiseCodeFilter && IsNoiseFilteredOperationCode(operationCode))
            {
                matchPath = string.Empty;
                matchValue = null;
                matchedText = null;
                return false;
            }

            return TryGetLogMatch(parameters, out matchPath, out matchValue, out matchedText);
        }

        private static bool IsNoiseFilteredEventCode(short eventCode)
        {
            for (int i = 0; i < NoiseFilteredEventCodes.Length; i++)
            {
                if (eventCode == (short)NoiseFilteredEventCodes[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNoiseFilteredOperationCode(short operationCode)
        {
            for (int i = 0; i < NoiseFilteredOperationCodes.Length; i++)
            {
                if (operationCode == (short)NoiseFilteredOperationCodes[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetLogMatch(Dictionary<byte, object> parameters, out string matchPath, out object? matchValue, out string? matchedText)
        {
            if (!EnableDeepParameterValueFilter)
            {
                matchPath = "filter-disabled";
                matchValue = null;
                matchedText = null;
                return true;
            }

            return TryFindAnyMatchingParameter(parameters, RequiredParameterTexts, out matchPath, out matchValue, out matchedText);
        }

        private static bool TryFindAnyMatchingParameter(Dictionary<byte, object> parameters, string[] requiredTexts, out string matchPath, out object? matchValue, out string? matchedText)
        {
            foreach (var parameter in parameters)
            {
                if (TryFindAnyText(parameter.Value, $"parameters[{parameter.Key}]", requiredTexts, out matchPath, out matchValue, out matchedText))
                {
                    return true;
                }
            }

            matchPath = string.Empty;
            matchValue = null;
            matchedText = null;
            return false;
        }

        private static bool TryFindAnyText(object? candidate, string candidatePath, string[] requiredTexts, out string matchPath, out object? matchValue, out string? matchedText)
        {
            if (candidate is null)
            {
                matchPath = string.Empty;
                matchValue = null;
                matchedText = null;
                return false;
            }

            if (candidate is string text && TryGetMatchingText(text, requiredTexts, out matchedText))
            {
                matchPath = candidatePath;
                matchValue = candidate;
                return true;
            }

            if (candidate is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (TryFindAnyText(entry.Value, $"{candidatePath}[{entry.Key}]", requiredTexts, out matchPath, out matchValue, out matchedText))
                    {
                        return true;
                    }
                }

                matchPath = string.Empty;
                matchValue = null;
                matchedText = null;
                return false;
            }

            if (candidate is IEnumerable enumerable)
            {
                int index = 0;
                foreach (var item in enumerable)
                {
                    if (TryFindAnyText(item, $"{candidatePath}[{index}]", requiredTexts, out matchPath, out matchValue, out matchedText))
                    {
                        return true;
                    }

                    index++;
                }

                matchPath = string.Empty;
                matchValue = null;
                matchedText = null;
                return false;
            }

            var candidateText = candidate.ToString();
            if (candidateText != null && TryGetMatchingText(candidateText, requiredTexts, out matchedText))
            {
                matchPath = candidatePath;
                matchValue = candidate;
                return true;
            }

            matchPath = string.Empty;
            matchValue = null;
            matchedText = null;
            return false;
        }

        private static bool TryGetMatchingText(string text, string[] requiredTexts, out string? matchedText)
        {
            foreach (var requiredText in requiredTexts)
            {
                if (!string.IsNullOrEmpty(requiredText) && text.IndexOf(requiredText, StringComparison.Ordinal) >= 0)
                {
                    matchedText = requiredText;
                    return true;
                }
            }

            matchedText = null;
            return false;
        }

        private static string DescribeParameterKeys(Dictionary<byte, object> parameters)
        {
            return string.Join(",", parameters.Keys.OrderBy(x => x));
        }

        private static string DescribePrimaryParameter(Dictionary<byte, object> parameters)
        {
            if (!parameters.TryGetValue(0, out object? value))
            {
                return "p0=missing";
            }

            return $"p0={DescribeValueShape(value)}";
        }

        private static string DescribeCodeParameter(Dictionary<byte, object> parameters, byte key)
        {
            if (!parameters.TryGetValue(key, out object? value))
            {
                return "missing";
            }

            string typeName = value?.GetType().Name ?? "null";
            if (!TryConvertToInt32(value, out int intValue))
            {
                return $"type={typeName} value={value}";
            }

            int unsigned16 = intValue & 0xFFFF;
            return $"type={typeName} dec={intValue} hex=0x{unsigned16:X4} div256={intValue / 256} shr8={unsigned16 >> 8} shr4={unsigned16 >> 4} hi=0x{((unsigned16 >> 8) & 0xFF):X2} lo=0x{(unsigned16 & 0xFF):X2}";
        }

        private static string DescribeValueShape(object? value)
        {
            if (value is null)
            {
                return "null";
            }

            if (value is string text)
            {
                return $"string(len={text.Length}, sample={Truncate(text, 80)})";
            }

            if (value is IDictionary dictionary)
            {
                return $"dictionary(count={dictionary.Count})";
            }

            if (value is IEnumerable enumerable)
            {
                int count = 0;
                object? first = null;
                foreach (var item in enumerable)
                {
                    if (count == 0)
                    {
                        first = item;
                    }

                    count++;
                    if (count >= 4)
                    {
                        break;
                    }
                }

                string firstDescription = first is null ? "null" : Truncate(first.ToString() ?? first.GetType().Name, 80);
                return $"{value.GetType().Name}(sampleCount={count}, first={firstDescription})";
            }

            return $"{value.GetType().Name}({Truncate(value.ToString() ?? string.Empty, 80)})";
        }

        private static bool TryConvertToInt32(object? value, out int intValue)
        {
            switch (value)
            {
                case byte byteValue:
                    intValue = byteValue;
                    return true;
                case short shortValue:
                    intValue = shortValue;
                    return true;
                case int actualIntValue:
                    intValue = actualIntValue;
                    return true;
                case Enum enumValue:
                    intValue = Convert.ToInt32(enumValue);
                    return true;
                default:
                    intValue = default;
                    return false;
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private static string GetEventName(short eventCode)
        {
            int code = eventCode;
            if (!Enum.IsDefined(typeof(EventCodes), code))
            {
                return $"Unknown({eventCode})";
            }

            return ((EventCodes)code).ToString();
        }

        private static string GetOperationName(short operationCode)
        {
            int code = operationCode;
            if (!Enum.IsDefined(typeof(OperationCodes), code))
            {
                return $"Unknown({operationCode})";
            }

            return ((OperationCodes)code).ToString();
        }
#endif

        private short ParseOperationCode(Dictionary<byte, object> parameters)
        {
            if (TryGetCodeParameter(parameters, 253, out short parameterOperationCode))
            {
                if (IsKnownOperationCode(parameterOperationCode))
                {
                    return parameterOperationCode;
                }

                throw new InvalidOperationException($"Unknown operation code in parameter 253: {parameterOperationCode}.");
            }

            throw new InvalidOperationException("Operation code parameter 253 is missing.");
        }

        private short ParseEventCode(Dictionary<byte, object> parameters)
        {
            if (TryGetCodeParameter(parameters, 252, out short parameterEventCode))
            {
                if (IsKnownEventCode(parameterEventCode))
                {
                    return parameterEventCode;
                }

                if (TryNormalizePackedEventCode(parameterEventCode, out short normalizedEventCode))
                {
                    return normalizedEventCode;
                }

                throw new InvalidOperationException($"Unknown event code in parameter 252: {parameterEventCode}.");
            }

            throw new InvalidOperationException("Event code parameter 252 is missing.");
        }

        private bool TryParseEventCode(Dictionary<byte, object> parameters, out short eventCode)
        {
            try
            {
                eventCode = ParseEventCode(parameters);
                return true;
            }
            catch (InvalidOperationException)
            {
                eventCode = default;
                return false;
            }
        }

        private static bool TryNormalizePackedEventCode(short packedEventCode, out short normalizedEventCode)
        {
            int unsignedValue = (ushort)packedEventCode;
            int shiftedByNibble = unsignedValue >> 4;

            if ((unsignedValue & 0x0F) == 0x01 && IsKnownEventCode(shiftedByNibble))
            {
                normalizedEventCode = (short)shiftedByNibble;
                return true;
            }

            normalizedEventCode = default;
            return false;
        }

        private static bool TryGetCodeParameter(Dictionary<byte, object> parameters, byte key, out short code)
        {
            if (!parameters.TryGetValue(key, out object value))
            {
                code = default;
                return false;
            }

            switch (value)
            {
                case short shortValue:
                    code = shortValue;
                    return true;
                case int intValue:
                    code = unchecked((short)intValue);
                    return true;
                case byte byteValue:
                    code = byteValue;
                    return true;
                case Enum enumValue:
                    code = Convert.ToInt16(enumValue);
                    return true;
                default:
                    throw new InvalidCastException($"Unable to cast object of type '{value.GetType()}' to Int16.");
            }
        }

        private static bool IsKnownOperationCode(int operationCode)
        {
            return Enum.IsDefined(typeof(OperationCodes), operationCode);
        }

        private static bool IsKnownEventCode(int eventCode)
        {
            return Enum.IsDefined(typeof(EventCodes), eventCode);
        }
    }
}
