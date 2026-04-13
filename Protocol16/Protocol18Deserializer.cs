using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Protocol16.Photon;

namespace Protocol16
{
    internal enum Protocol18Type : byte
    {
        Unknown = 0,
        Boolean = 2,
        Byte = 3,
        Short = 4,
        Float = 5,
        Double = 6,
        String = 7,
        Null = 8,
        CompressedInt = 9,
        CompressedLong = 10,
        Int1 = 11,
        Int1Negative = 12,
        Int2 = 13,
        Int2Negative = 14,
        Long1 = 15,
        Long1Negative = 16,
        Long2 = 17,
        Long2Negative = 18,
        Custom = 19,
        Dictionary = 20,
        Hashtable = 21,
        ObjectArray = 23,
        OperationRequest = 24,
        OperationResponse = 25,
        EventData = 26,
        BooleanFalse = 27,
        BooleanTrue = 28,
        ShortZero = 29,
        IntZero = 30,
        LongZero = 31,
        FloatZero = 32,
        DoubleZero = 33,
        ByteZero = 34,
        Array = 0x40,
        CustomTypeSlim = 0x80,
    }

    public static class Protocol18Deserializer
    {
        public static OperationRequest DeserializeOperationRequest(PhotonPacketStream input)
        {
            byte operationCode = ReadByte(input);
            Dictionary<byte, object> parameters = DeserializeParameterTable(input);

            return new OperationRequest(operationCode, parameters);
        }

        public static OperationResponse DeserializeOperationResponse(PhotonPacketStream input)
        {
            byte operationCode = ReadByte(input);
            short returnCode = ReadInt16(input);
            string debugMessage = string.Empty;

            if (Remaining(input) > 0)
            {
                object debugValue = Deserialize(input, ReadByte(input));
                debugMessage = debugValue as string ?? string.Empty;
            }

            Dictionary<byte, object> parameters = DeserializeParameterTable(input);

            return new OperationResponse(operationCode, returnCode, debugMessage, parameters);
        }

        public static EventData DeserializeEventData(PhotonPacketStream input)
        {
            byte code = ReadByte(input);
            Dictionary<byte, object> parameters = DeserializeParameterTable(input);

            return new EventData(code, parameters);
        }

        public static object Deserialize(PhotonPacketStream input)
        {
            return Deserialize(input, ReadByte(input));
        }

        public static object Deserialize(PhotonPacketStream input, byte typeCode)
        {
            if (typeCode >= (byte)Protocol18Type.CustomTypeSlim)
            {
                return DeserializeCustom(input, typeCode);
            }

            switch ((Protocol18Type)typeCode)
            {
                case Protocol18Type.Unknown:
                case Protocol18Type.Null:
                    return null;
                case Protocol18Type.Boolean:
                    return ReadByte(input) != 0;
                case Protocol18Type.Byte:
                    return ReadByte(input);
                case Protocol18Type.Short:
                    return ReadInt16(input);
                case Protocol18Type.Float:
                    return ReadSingle(input);
                case Protocol18Type.Double:
                    return ReadDouble(input);
                case Protocol18Type.String:
                    return ReadString(input);
                case Protocol18Type.CompressedInt:
                    return ReadCompressedInt32(input);
                case Protocol18Type.CompressedLong:
                    return ReadCompressedInt64(input);
                case Protocol18Type.Int1:
                    return (int)ReadByte(input);
                case Protocol18Type.Int1Negative:
                    return -(int)ReadByte(input);
                case Protocol18Type.Int2:
                    return (int)ReadUInt16(input);
                case Protocol18Type.Int2Negative:
                    return -(int)ReadUInt16(input);
                case Protocol18Type.Long1:
                    return (long)ReadByte(input);
                case Protocol18Type.Long1Negative:
                    return -(long)ReadByte(input);
                case Protocol18Type.Long2:
                    return (long)ReadUInt16(input);
                case Protocol18Type.Long2Negative:
                    return -(long)ReadUInt16(input);
                case Protocol18Type.Custom:
                    return DeserializeCustom(input, 0);
                case Protocol18Type.Dictionary:
                    return DeserializeDictionary(input);
                case Protocol18Type.Hashtable:
                    return DeserializeHashtable(input);
                case Protocol18Type.ObjectArray:
                    return DeserializeObjectArray(input);
                case Protocol18Type.OperationRequest:
                    return DeserializeOperationRequest(input);
                case Protocol18Type.OperationResponse:
                    return DeserializeOperationResponse(input);
                case Protocol18Type.EventData:
                    return DeserializeEventData(input);
                case Protocol18Type.BooleanFalse:
                    return false;
                case Protocol18Type.BooleanTrue:
                    return true;
                case Protocol18Type.ShortZero:
                    return (short)0;
                case Protocol18Type.IntZero:
                    return 0;
                case Protocol18Type.LongZero:
                    return 0L;
                case Protocol18Type.FloatZero:
                    return 0f;
                case Protocol18Type.DoubleZero:
                    return 0d;
                case Protocol18Type.ByteZero:
                    return (byte)0;
                case Protocol18Type.Array:
                    return DeserializeNestedArray(input);
                default:
                    if ((typeCode & (byte)Protocol18Type.Array) == (byte)Protocol18Type.Array)
                    {
                        return DeserializeTypedArray(input, (byte)(typeCode & ~(byte)Protocol18Type.Array));
                    }

                    throw new ArgumentException($"Type code: {typeCode} not implemented.");
            }
        }

        private static Dictionary<byte, object> DeserializeParameterTable(PhotonPacketStream input)
        {
            int dictionarySize = ReadCount(input);
            var dictionary = new Dictionary<byte, object>(dictionarySize);

            for (int i = 0; i < dictionarySize; i++)
            {
                byte key = ReadByte(input);
                byte valueTypeCode = ReadByte(input);
                object value;
                try
                {
                    value = Deserialize(input, valueTypeCode);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"Failed to deserialize parameter key={key} valueType=0x{valueTypeCode:X2} remaining={Remaining(input)}.",
                        ex);
                }

                dictionary[key] = value;
            }

            return dictionary;
        }

        private static IDictionary DeserializeDictionary(PhotonPacketStream input)
        {
            byte keyTypeCode = ReadByte(input);
            byte valueTypeCode = ReadByte(input);
            int dictionarySize = ReadCount(input);
            IDictionary output = new Dictionary<object, object>(dictionarySize);

            for (int i = 0; i < dictionarySize; i++)
            {
                object key = Deserialize(input, keyTypeCode == 0 ? ReadByte(input) : keyTypeCode);
                object value = Deserialize(input, valueTypeCode == 0 ? ReadByte(input) : valueTypeCode);
                output.Add(key, value);
            }

            return output;
        }

        private static Hashtable DeserializeHashtable(PhotonPacketStream input)
        {
            byte keyTypeCode = ReadByte(input);
            byte valueTypeCode = ReadByte(input);
            int dictionarySize = ReadCount(input);
            var output = new Hashtable(dictionarySize);

            for (int i = 0; i < dictionarySize; i++)
            {
                object key = Deserialize(input, keyTypeCode == 0 ? ReadByte(input) : keyTypeCode);
                object value = Deserialize(input, valueTypeCode == 0 ? ReadByte(input) : valueTypeCode);
                output.Add(key, value);
            }

            return output;
        }

        private static object[] DeserializeObjectArray(PhotonPacketStream input)
        {
            int size = ReadCount(input);
            var result = new object[size];

            for (int i = 0; i < size; i++)
            {
                result[i] = Deserialize(input);
            }

            return result;
        }

        private static Array DeserializeNestedArray(PhotonPacketStream input)
        {
            int size = ReadCount(input);
            byte typeCode = ReadByte(input);
            var result = new object[size];

            for (int i = 0; i < size; i++)
            {
                long itemStart = input.Position;
                try
                {
                    result[i] = Deserialize(input, typeCode);
                }
                catch (Exception ex)
                {
                    input.Position = itemStart;
                    if (TryDeserializeNestedItemWithRepeatedTypeCode(input, typeCode, out object repeatedTypeValue))
                    {
                        result[i] = repeatedTypeValue;
                        continue;
                    }

                    input.Position = itemStart;
                    throw new ArgumentException(
                        $"Failed to deserialize nested array item index={i} type=0x{typeCode:X2} size={size} remaining={Remaining(input)}.",
                        ex);
                }
            }

            return result;
        }

        private static Array DeserializeTypedArray(PhotonPacketStream input, byte elementTypeCode)
        {
            int size = ReadCount(input);
            try
            {
                switch ((Protocol18Type)elementTypeCode)
                {
                    case Protocol18Type.Boolean:
                        {
                            var result = new bool[size];
                            int packedByteCount = (size + 7) / 8;
                            byte[] packed = ReadBytes(input, packedByteCount);

                            for (int i = 0; i < size; i++)
                            {
                                int byteIndex = i / 8;
                                int bitIndex = i % 8;
                                result[i] = (packed[byteIndex] & (1 << bitIndex)) != 0;
                            }
                            return result;
                        }
                    case Protocol18Type.Byte:
                        return ReadBytes(input, size);
                    case Protocol18Type.Short:
                        {
                            var result = new short[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadInt16(input);
                            }
                            return result;
                        }
                    case Protocol18Type.Float:
                        {
                            var result = new float[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadSingle(input);
                            }
                            return result;
                        }
                    case Protocol18Type.Double:
                        {
                            var result = new double[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadDouble(input);
                            }
                            return result;
                        }
                    case Protocol18Type.String:
                        {
                            var result = new string[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadString(input);
                            }
                            return result;
                        }
                    case Protocol18Type.Custom:
                        {
                            byte customType = ReadByte(input);
                            var result = new object[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = DeserializeCustomPayload(input, customType);
                            }
                            return result;
                        }
                    case Protocol18Type.Dictionary:
                        {
                            var result = new object[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = DeserializeDictionary(input);
                            }
                            return result;
                        }
                    case Protocol18Type.Hashtable:
                        {
                            var result = new object[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = DeserializeHashtable(input);
                            }
                            return result;
                        }
                    case Protocol18Type.CompressedInt:
                        {
                            var result = new int[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadCompressedInt32(input);
                            }
                            return result;
                        }
                    case Protocol18Type.CompressedLong:
                        {
                            var result = new long[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = ReadCompressedInt64(input);
                            }
                            return result;
                        }
                    default:
                        {
                            var result = new object[size];
                            for (int i = 0; i < size; i++)
                            {
                                result[i] = Deserialize(input, elementTypeCode);
                            }
                            return result;
                        }
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Failed to deserialize typed array elementType=0x{elementTypeCode:X2} size={size} remaining={Remaining(input)}.",
                    ex);
            }
        }

        private static bool TryDeserializeNestedItemWithRepeatedTypeCode(PhotonPacketStream input, byte typeCode, out object value)
        {
            long start = input.Position;

            try
            {
                if (!IsNestedCompressedArrayType(typeCode) || ReadByte(input) != typeCode)
                {
                    input.Position = start;
                    value = null!;
                    return false;
                }

                value = Deserialize(input, typeCode);
                return true;
            }
            catch
            {
                input.Position = start;
                value = null!;
                return false;
            }
        }

        private static bool IsNestedCompressedArrayType(byte typeCode)
        {
            return typeCode == ((byte)Protocol18Type.Array | (byte)Protocol18Type.CompressedInt)
                || typeCode == ((byte)Protocol18Type.Array | (byte)Protocol18Type.CompressedLong);
        }

        private static object DeserializeCustom(PhotonPacketStream input, byte gpType)
        {
            byte customType = gpType >= (byte)Protocol18Type.CustomTypeSlim
                ? (byte)(gpType & 0x7F)
                : ReadByte(input);
            bool isSlimCustomType = gpType >= (byte)Protocol18Type.CustomTypeSlim;
            return DeserializeCustomPayload(input, customType, isSlimCustomType);
        }

        private static byte[] DeserializeCustomPayload(PhotonPacketStream input, byte customType, bool isSlimCustomType = false)
        {
            long start = input.Position;
            int size = ReadCount(input);

            if (size < 0 || size > Remaining(input))
            {
                if (isSlimCustomType)
                {
                    input.Position = start;
                    return ReadBytes(input, Remaining(input));
                }

                throw new ArgumentException($"Custom type {customType} reported invalid size {size}.");
            }

            return ReadBytes(input, size);
        }

        private static string ReadString(PhotonPacketStream input)
        {
            long start = input.Position;

            if (TryReadCompressedLength(input, out int compressedLength) && compressedLength <= Remaining(input))
            {
                return Encoding.UTF8.GetString(ReadBytes(input, compressedLength), 0, compressedLength);
            }

            input.Position = start;
            byte lengthType = ReadByte(input);
            int length;

            switch (lengthType)
            {
                case 0:
                    return string.Empty;
                case 1:
                    length = ReadByte(input);
                    break;
                case 2:
                    length = ReadUInt16(input);
                    break;
                case 4:
                    length = ReadInt32(input);
                    break;
                default:
                    throw new ArgumentException($"Received string type with unsupported length: {lengthType}");
            }

            if (length < 0 || length > Remaining(input))
            {
                throw new ArgumentException($"Received invalid string length: {length}");
            }

            return Encoding.UTF8.GetString(ReadBytes(input, length), 0, length);
        }

        private static bool TryReadCompressedLength(PhotonPacketStream input, out int value)
        {
            long start = input.Position;

            try
            {
                uint compressed = ReadCompressedUInt32(input);
                if (compressed > int.MaxValue)
                {
                    value = 0;
                    input.Position = start;
                    return false;
                }

                value = (int)compressed;
                return true;
            }
            catch
            {
                value = 0;
                input.Position = start;
                return false;
            }
        }

        private static int ReadCount(PhotonPacketStream input)
        {
            if (TryReadCompressedLength(input, out int count) && count <= Remaining(input) + 1024)
            {
                return count;
            }

            throw new ArgumentException("Failed to read compressed Protocol18 count.");
        }

        private static byte[] ReadBytes(PhotonPacketStream input, int count)
        {
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[count];
            int read = input.Read(buffer, 0, count);
            if (read != count)
            {
                throw new ArgumentException($"Unable to read {count} byte(s); got {read}.");
            }

            return buffer;
        }

        private static byte ReadByte(PhotonPacketStream input)
        {
            int value = input.ReadByte();
            if (value < 0)
            {
                throw new ArgumentException("Unexpected end of stream.");
            }

            return (byte)value;
        }

        private static short ReadInt16(PhotonPacketStream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(short));
            return (short)(buffer[0] | (buffer[1] << 8));
        }

        private static int ReadUInt16(PhotonPacketStream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(short));
            return buffer[0] | (buffer[1] << 8);
        }

        private static int ReadInt32(PhotonPacketStream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(int));
            return buffer[0] << 24 | buffer[1] << 16 | buffer[2] << 8 | buffer[3];
        }

        private static float ReadSingle(PhotonPacketStream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(float));
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }

            return BitConverter.ToSingle(buffer, 0);
        }

        private static double ReadDouble(PhotonPacketStream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(double));
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }

            return BitConverter.ToDouble(buffer, 0);
        }

        private static uint ReadCompressedUInt32(PhotonPacketStream input)
        {
            uint value = 0;
            int shift = 0;

            while (shift < 35)
            {
                byte current = ReadByte(input);
                value |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new ArgumentException("Compressed UInt32 is too large.");
        }

        private static ulong ReadCompressedUInt64(PhotonPacketStream input)
        {
            ulong value = 0;
            int shift = 0;

            while (shift < 70)
            {
                byte current = ReadByte(input);
                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new ArgumentException("Compressed UInt64 is too large.");
        }

        private static int ReadCompressedInt32(PhotonPacketStream input)
        {
            uint value = ReadCompressedUInt32(input);
            return (int)((value >> 1) ^ (uint)-(int)(value & 1));
        }

        private static long ReadCompressedInt64(PhotonPacketStream input)
        {
            ulong value = ReadCompressedUInt64(input);
            return (long)((value >> 1) ^ (ulong)-(long)(value & 1));
        }

        private static int Remaining(PhotonPacketStream input)
        {
            return (int)(input.Length - input.Position);
        }
    }
}
