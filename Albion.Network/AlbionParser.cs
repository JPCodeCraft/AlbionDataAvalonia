using AlbionDataAvalonia.Shared;
using PhotonPackageParser;
using System;
using System.Collections.Generic;
#if DEBUG
using Serilog;
using System.Collections;
#endif

namespace Albion.Network
{
    internal sealed class AlbionParser : PhotonParser, IPhotonReceiver
    {
        private readonly HandlersCollection handlers;
#if DEBUG
        // Debug packet logging settings.
        private const bool EnableParserDebugPacketLogging = true;
        // Toggle this deep scan on/off as needed. It is expensive on busy streams.
        private const bool EnableDeepParameterValueFilter = true;
        private const bool EnableNoiseCodeFilter = true;
        private static readonly string[] RequiredParameterTexts = new[] { "0.156", "156", "15.6", "865", "86.5", "864", "86,4", "2312" };
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

        protected override void OnEvent(byte Code, Dictionary<byte, object> Parameters)
        {
            if (Code == 3)
            {
                Parameters.Add(252, (short)EventCodes.Move);
            }

            short eventCode = ParseEventCode(Parameters);
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
