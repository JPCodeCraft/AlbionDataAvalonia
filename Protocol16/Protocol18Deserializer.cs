using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        BooleanArray = 66,
        ByteArray = 67,
        ShortArray = 68,
        FloatArray = 69,
        DoubleArray = 70,
        StringArray = 71,
        CompressedIntArray = 73,
        CompressedLongArray = 74,
        CustomTypeArray = 83,
        DictionaryArray = 84,
        HashtableArray = 85,
        CustomTypeSlim = 0x80,
    }

    public static class Protocol18Deserializer
    {
        private const byte MaxSlimCustomTypeCode = 228;

        private static readonly byte[] BoolMasks =
        {
            1,
            2,
            4,
            8,
            16,
            32,
            64,
            128
        };

        public static OperationRequest DeserializeOperationRequest(Stream input)
        {
            byte operationCode = ReadByte(input);
            Dictionary<byte, object> parameters = DeserializeParameterTable(input);

            return new OperationRequest(operationCode, parameters);
        }

        public static OperationResponse DeserializeOperationResponse(Stream input)
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

        public static EventData DeserializeEventData(Stream input)
        {
            byte code = ReadByte(input);
            Dictionary<byte, object> parameters = DeserializeParameterTable(input);

            return new EventData(code, parameters);
        }

        public static object Deserialize(Stream input)
        {
            return Deserialize(input, ReadByte(input));
        }

        public static object Deserialize(Stream input, byte typeCode)
        {
            if (typeCode >= (byte)Protocol18Type.CustomTypeSlim && typeCode <= MaxSlimCustomTypeCode)
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
                    return DeserializeArrayInArray(input);
                case Protocol18Type.BooleanArray:
                    return DeserializeBooleanArray(input);
                case Protocol18Type.ByteArray:
                    return DeserializeByteArray(input);
                case Protocol18Type.ShortArray:
                    return DeserializeShortArray(input);
                case Protocol18Type.FloatArray:
                    return DeserializeFloatArray(input);
                case Protocol18Type.DoubleArray:
                    return DeserializeDoubleArray(input);
                case Protocol18Type.StringArray:
                    return DeserializeStringArray(input);
                case Protocol18Type.CompressedIntArray:
                    return DeserializeCompressedIntArray(input);
                case Protocol18Type.CompressedLongArray:
                    return DeserializeCompressedLongArray(input);
                case Protocol18Type.CustomTypeArray:
                    return DeserializeCustomTypeArray(input);
                case Protocol18Type.DictionaryArray:
                    return DeserializeDictionaryArray(input);
                case Protocol18Type.HashtableArray:
                    return DeserializeHashtableArray(input);
                default:
                    throw new ArgumentException($"Type code: {typeCode} not implemented.");
            }
        }

        private static Dictionary<byte, object> DeserializeParameterTable(Stream input)
        {
            int dictionarySize = ReadByte(input);
            var dictionary = new Dictionary<byte, object>(dictionarySize);

            for (int i = 0; i < dictionarySize; i++)
            {
                long parameterStart = input.Position;
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
                        $"Failed to deserialize parameter index={i} key={key} valueType=0x{valueTypeCode:X2} position={parameterStart} remaining={Remaining(input)} next=\"{PeekHex(input)}\".",
                        ex);
                }

                dictionary[key] = value;
            }

            return dictionary;
        }

        private static IDictionary DeserializeDictionary(Stream input)
        {
            Type dictionaryType = DeserializeDictionaryType(input, out Protocol18Type keyTypeCode, out Protocol18Type valueTypeCode);
            if (!(Activator.CreateInstance(dictionaryType) is IDictionary dictionary))
            {
                throw new InvalidOperationException($"Could not create dictionary type '{dictionaryType}'.");
            }

            DeserializeDictionaryElements(input, dictionary, keyTypeCode, valueTypeCode);
            return dictionary;
        }

        private static Hashtable DeserializeHashtable(Stream input)
        {
            int size = ReadCount(input);
            var output = new Hashtable(size);

            for (int i = 0; i < size; i++)
            {
                object key = Deserialize(input);
                object value = Deserialize(input);
                if (key != null)
                {
                    output[key] = value;
                }
            }

            return output;
        }

        private static object[] DeserializeObjectArray(Stream input)
        {
            int size = ReadCount(input);
            var result = new object[size];

            for (int i = 0; i < size; i++)
            {
                result[i] = Deserialize(input);
            }

            return result;
        }

        private static byte[] DeserializeByteArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            return ReadBytes(input, arrayLength);
        }

        private static short[] DeserializeShortArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new short[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = ReadInt16(input);
            }

            return array;
        }

        private static float[] DeserializeFloatArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            int byteLength = checked(arrayLength * sizeof(float));
            var array = new float[arrayLength];
            if (byteLength == 0)
            {
                return array;
            }

            byte[] buffer = ReadBytes(input, byteLength);
            if (!BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < byteLength; i += sizeof(float))
                {
                    Array.Reverse(buffer, i, sizeof(float));
                }
            }

            Buffer.BlockCopy(buffer, 0, array, 0, byteLength);

            return array;
        }

        private static double[] DeserializeDoubleArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            int byteLength = checked(arrayLength * sizeof(double));
            var array = new double[arrayLength];
            if (byteLength == 0)
            {
                return array;
            }

            byte[] buffer = ReadBytes(input, byteLength);
            if (!BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < byteLength; i += sizeof(double))
                {
                    Array.Reverse(buffer, i, sizeof(double));
                }
            }

            Buffer.BlockCopy(buffer, 0, array, 0, byteLength);

            return array;
        }

        private static bool[] DeserializeBooleanArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new bool[arrayLength];
            int fullByteCount = arrayLength / 8;
            int index = 0;

            for (int i = 0; i < fullByteCount; i++)
            {
                byte value = ReadByte(input);
                array[index++] = (value & 1) == 1;
                array[index++] = (value & 2) == 2;
                array[index++] = (value & 4) == 4;
                array[index++] = (value & 8) == 8;
                array[index++] = (value & 16) == 16;
                array[index++] = (value & 32) == 32;
                array[index++] = (value & 64) == 64;
                array[index++] = (value & 128) == 128;
            }

            if (index < arrayLength)
            {
                byte value = ReadByte(input);
                int bitIndex = 0;
                while (index < arrayLength)
                {
                    array[index++] = (value & BoolMasks[bitIndex]) == BoolMasks[bitIndex];
                    bitIndex++;
                }
            }

            return array;
        }

        private static string[] DeserializeStringArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new string[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = ReadString(input);
            }

            return array;
        }

        private static int[] DeserializeCompressedIntArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new int[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = ReadCompressedInt32(input);
            }

            return array;
        }

        private static long[] DeserializeCompressedLongArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new long[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = ReadCompressedInt64(input);
            }

            return array;
        }

        private static Protocol18CustomType[] DeserializeCustomTypeArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            byte typeCode = ReadByte(input);
            var array = new Protocol18CustomType[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = DeserializeCustomPayload(input, typeCode);
            }

            return array;
        }

        private static Hashtable[] DeserializeHashtableArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            var array = new Hashtable[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                array[i] = DeserializeHashtable(input);
            }

            return array;
        }

        private static IDictionary[] DeserializeDictionaryArray(Stream input)
        {
            Type dictionaryType = DeserializeDictionaryType(input, out Protocol18Type keyTypeCode, out Protocol18Type valueTypeCode);
            int arrayLength = ReadCount(input);
            var array = (IDictionary[])Array.CreateInstance(dictionaryType, arrayLength);

            for (int i = 0; i < arrayLength; i++)
            {
                if (!(Activator.CreateInstance(dictionaryType) is IDictionary dictionary))
                {
                    throw new InvalidOperationException($"Could not create dictionary type '{dictionaryType}'.");
                }

                DeserializeDictionaryElements(input, dictionary, keyTypeCode, valueTypeCode);
                array[i] = dictionary;
            }

            return array;
        }

        private static Array? DeserializeArrayInArray(Stream input)
        {
            int arrayLength = ReadCount(input);
            Array? result = null;
            Type? resultType = null;

            for (int i = 0; i < arrayLength; i++)
            {
                object value = Deserialize(input);
                if (!(value is Array nestedArray))
                {
                    continue;
                }

                if (result == null)
                {
                    resultType = nestedArray.GetType();
                    result = Array.CreateInstance(resultType, arrayLength);
                }

                if (resultType != null && resultType.IsAssignableFrom(nestedArray.GetType()))
                {
                    result.SetValue(nestedArray, i);
                }
            }

            return result;
        }

        private static void DeserializeDictionaryElements(Stream input, IDictionary dictionary, Protocol18Type keyTypeCode, Protocol18Type valueTypeCode)
        {
            int size = ReadCount(input);
            for (int i = 0; i < size; i++)
            {
                object key = keyTypeCode == Protocol18Type.Unknown
                    ? Deserialize(input)
                    : Deserialize(input, (byte)keyTypeCode);
                object value = valueTypeCode == Protocol18Type.Unknown
                    ? Deserialize(input)
                    : Deserialize(input, (byte)valueTypeCode);

                if (key != null)
                {
                    dictionary.Add(key, value);
                }
            }
        }

        private static Type DeserializeDictionaryType(Stream input, out Protocol18Type keyTypeCode, out Protocol18Type valueTypeCode)
        {
            keyTypeCode = (Protocol18Type)ReadByte(input);
            valueTypeCode = (Protocol18Type)ReadByte(input);

            Type keyType = keyTypeCode == Protocol18Type.Unknown
                ? typeof(object)
                : GetAllowedDictionaryKeyType(keyTypeCode);

            Type valueType = valueTypeCode switch
            {
                Protocol18Type.Unknown => typeof(object),
                Protocol18Type.Dictionary => DeserializeDictionaryType(input),
                Protocol18Type.Array => GetDictionaryArrayType(input),
                Protocol18Type.ObjectArray => typeof(object[]),
                Protocol18Type.HashtableArray => typeof(Hashtable[]),
                _ => GetClrArrayType(valueTypeCode),
            };

            if (valueTypeCode == Protocol18Type.Array)
            {
                valueTypeCode = Protocol18Type.Unknown;
            }

            return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        }

        private static Type DeserializeDictionaryType(Stream input)
        {
            Protocol18Type keyTypeCode = (Protocol18Type)ReadByte(input);
            Protocol18Type valueTypeCode = (Protocol18Type)ReadByte(input);

            Type keyType = keyTypeCode == Protocol18Type.Unknown
                ? typeof(object)
                : GetAllowedDictionaryKeyType(keyTypeCode);

            Type valueType = valueTypeCode switch
            {
                Protocol18Type.Unknown => typeof(object),
                Protocol18Type.Dictionary => DeserializeDictionaryType(input),
                Protocol18Type.Array => GetDictionaryArrayType(input),
                _ => GetClrArrayType(valueTypeCode),
            };

            return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        }

        private static Type GetDictionaryArrayType(Stream input)
        {
            Protocol18Type typeCode = (Protocol18Type)ReadByte(input);
            int nestedArrayDepth = 0;

            while (typeCode == Protocol18Type.Array)
            {
                nestedArrayDepth++;
                typeCode = (Protocol18Type)ReadByte(input);
            }

            Type arrayType = GetClrArrayType(typeCode).MakeArrayType();
            for (int i = 0; i < nestedArrayDepth; i++)
            {
                arrayType = arrayType.MakeArrayType();
            }

            return arrayType;
        }

        private static Type GetAllowedDictionaryKeyType(Protocol18Type typeCode)
        {
            switch (typeCode)
            {
                case Protocol18Type.Byte:
                case Protocol18Type.ByteZero:
                    return typeof(byte);
                case Protocol18Type.Short:
                case Protocol18Type.ShortZero:
                    return typeof(short);
                case Protocol18Type.Float:
                case Protocol18Type.FloatZero:
                    return typeof(float);
                case Protocol18Type.Double:
                case Protocol18Type.DoubleZero:
                    return typeof(double);
                case Protocol18Type.String:
                    return typeof(string);
                case Protocol18Type.CompressedInt:
                case Protocol18Type.Int1:
                case Protocol18Type.Int1Negative:
                case Protocol18Type.Int2:
                case Protocol18Type.Int2Negative:
                case Protocol18Type.IntZero:
                    return typeof(int);
                case Protocol18Type.CompressedLong:
                case Protocol18Type.Long1:
                case Protocol18Type.Long1Negative:
                case Protocol18Type.Long2:
                case Protocol18Type.Long2Negative:
                case Protocol18Type.LongZero:
                    return typeof(long);
                default:
                    throw new InvalidDataException($"Protocol18 type '{typeCode}' is not valid as a dictionary key.");
            }
        }

        private static Type GetClrArrayType(Protocol18Type typeCode)
        {
            switch (typeCode)
            {
                case Protocol18Type.Boolean:
                case Protocol18Type.BooleanFalse:
                case Protocol18Type.BooleanTrue:
                    return typeof(bool);
                case Protocol18Type.Byte:
                case Protocol18Type.ByteZero:
                    return typeof(byte);
                case Protocol18Type.Short:
                case Protocol18Type.ShortZero:
                    return typeof(short);
                case Protocol18Type.Float:
                case Protocol18Type.FloatZero:
                    return typeof(float);
                case Protocol18Type.Double:
                case Protocol18Type.DoubleZero:
                    return typeof(double);
                case Protocol18Type.String:
                    return typeof(string);
                case Protocol18Type.CompressedInt:
                case Protocol18Type.Int1:
                case Protocol18Type.Int1Negative:
                case Protocol18Type.Int2:
                case Protocol18Type.Int2Negative:
                case Protocol18Type.IntZero:
                    return typeof(int);
                case Protocol18Type.CompressedLong:
                case Protocol18Type.Long1:
                case Protocol18Type.Long1Negative:
                case Protocol18Type.Long2:
                case Protocol18Type.Long2Negative:
                case Protocol18Type.LongZero:
                    return typeof(long);
                case Protocol18Type.Custom:
                    return typeof(Protocol18CustomType);
                case Protocol18Type.Dictionary:
                    return typeof(IDictionary);
                case Protocol18Type.Hashtable:
                    return typeof(Hashtable);
                case Protocol18Type.OperationRequest:
                    return typeof(OperationRequest);
                case Protocol18Type.OperationResponse:
                    return typeof(OperationResponse);
                case Protocol18Type.EventData:
                    return typeof(EventData);
                case Protocol18Type.BooleanArray:
                    return typeof(bool[]);
                case Protocol18Type.ByteArray:
                    return typeof(byte[]);
                case Protocol18Type.ShortArray:
                    return typeof(short[]);
                case Protocol18Type.FloatArray:
                    return typeof(float[]);
                case Protocol18Type.DoubleArray:
                    return typeof(double[]);
                case Protocol18Type.StringArray:
                    return typeof(string[]);
                case Protocol18Type.ObjectArray:
                    return typeof(object[]);
                case Protocol18Type.CustomTypeArray:
                    return typeof(Protocol18CustomType[]);
                case Protocol18Type.DictionaryArray:
                    return typeof(IDictionary[]);
                case Protocol18Type.HashtableArray:
                    return typeof(Hashtable[]);
                case Protocol18Type.CompressedIntArray:
                    return typeof(int[]);
                case Protocol18Type.CompressedLongArray:
                    return typeof(long[]);
                default:
                    throw new InvalidDataException($"Protocol18 type '{typeCode}' cannot be mapped to a CLR array type.");
            }
        }

        private static object DeserializeCustom(Stream input, byte gpType)
        {
            byte customType = gpType == (byte)Protocol18Type.Custom
                ? ReadByte(input)
                : (byte)(gpType - (byte)Protocol18Type.CustomTypeSlim);
            return DeserializeCustomPayload(input, customType);
        }

        private static Protocol18CustomType DeserializeCustomPayload(Stream input, byte customType)
        {
            int size = ReadCount(input);
            byte[] data = ReadBytes(input, size);
            return new Protocol18CustomType(customType, data);
        }

        private static string ReadString(Stream input)
        {
            int length = ReadCount(input);
            if (length == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(ReadBytes(input, length), 0, length);
        }

        private static int ReadCount(Stream input)
        {
            return checked((int)ReadCompressedUInt32(input));
        }

        private static byte[] ReadBytes(Stream input, int count)
        {
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[count];
            ReadExactly(input, buffer, count);
            return buffer;
        }

        private static void ReadExactly(Stream input, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Failed to read {count} bytes from the Protocol18 payload.");
                }

                offset += read;
            }
        }

        private static byte ReadByte(Stream input)
        {
            int value = input.ReadByte();
            if (value < 0)
            {
                throw new ArgumentException("Unexpected end of stream.");
            }

            return (byte)value;
        }

        private static short ReadInt16(Stream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(short));
            return (short)(buffer[0] | (buffer[1] << 8));
        }

        private static int ReadUInt16(Stream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(short));
            return buffer[0] | (buffer[1] << 8);
        }

        private static int ReadInt32(Stream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(int));
            return buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
        }

        private static float ReadSingle(Stream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(float));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer, 0, sizeof(float));
            }

            return BitConverter.ToSingle(buffer, 0);
        }

        private static double ReadDouble(Stream input)
        {
            byte[] buffer = ReadBytes(input, sizeof(double));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer, 0, sizeof(double));
            }

            return BitConverter.ToDouble(buffer, 0);
        }

        private static uint ReadCompressedUInt32(Stream input)
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

        private static ulong ReadCompressedUInt64(Stream input)
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

        private static int ReadCompressedInt32(Stream input)
        {
            uint value = ReadCompressedUInt32(input);
            return (int)((value >> 1) ^ (uint)-(int)(value & 1));
        }

        private static long ReadCompressedInt64(Stream input)
        {
            ulong value = ReadCompressedUInt64(input);
            return (long)((value >> 1) ^ (ulong)-(long)(value & 1));
        }

        private static int Remaining(Stream input)
        {
            return (int)(input.Length - input.Position);
        }

        private static string PeekHex(Stream input, int maxBytes = 16)
        {
            if (!input.CanSeek)
            {
                return string.Empty;
            }

            long start = input.Position;
            int count = Math.Min(maxBytes, Remaining(input));
            byte[] buffer = ReadBytes(input, count);
            input.Position = start;

            var builder = new StringBuilder(count * 3);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(buffer[i].ToString("X2"));
            }

            if (Remaining(input) > count)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }
    }

    internal sealed class Protocol18CustomType
    {
        public Protocol18CustomType(byte typeCode, byte[] data)
        {
            TypeCode = typeCode;
            Data = data;
        }

        public byte TypeCode { get; }

        public byte[] Data { get; }

        public override string ToString()
        {
            return $"Protocol18CustomType({TypeCode}, {Data.Length} bytes)";
        }
    }
}
