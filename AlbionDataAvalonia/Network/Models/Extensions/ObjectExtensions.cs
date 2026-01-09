using System;
using System.Collections;
using System.Linq;

public static class ObjectExtensions
{
    public static byte ToByte(this object obj)
    {
        if (obj is byte b) return b;
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to byte.");
    }

    public static short ToShort(this object obj)
    {
        if (obj is byte b) return b;
        if (obj is short s) return s;
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to short.");
    }

    public static int ToInt(this object obj)
    {
        if (obj is byte b) return b;
        if (obj is short s) return s;
        if (obj is int i) return i;
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to int.");
    }

    public static long ToLong(this object obj)
    {
        if (obj is byte b) return b;
        if (obj is short s) return s;
        if (obj is int i) return i;
        if (obj is long l) return l;
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to long.");
    }

    public static bool ToBool(this object obj)
    {
        if (obj is bool b) return b;
        if (Convert.ToBoolean(obj) is bool result) return result;
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to bool.");
    }

    public static Guid? ToGuid(this object obj)
    {
        if (obj is IEnumerable objEnumerable)
        {
            var myBytes = objEnumerable.OfType<byte>().ToArray();
            return new Guid(myBytes);
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to Guid.");
    }

    public static float[] ToFloatArray(this object obj)
    {
        if (obj is float[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => Convert.ToSingle(x)).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to float array.");
    }

    public static double[] ToDoubleArray(this object obj)
    {
        if (obj is double[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => Convert.ToDouble(x)).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to double array.");
    }

    public static int[] ToIntArray(this object obj)
    {
        if (obj is int[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToInt()).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to int array.");
    }

    public static long[] ToLongArray(this object obj)
    {
        if (obj is long[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToLong()).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to long array.");
    }

    public static short[] ToShortArray(this object obj)
    {
        if (obj is short[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToShort()).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to short array.");
    }

    public static byte[] ToByteArray(this object obj)
    {
        if (obj is byte[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToByte()).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to byte array.");
    }

    public static bool[] ToBoolArray(this object obj)
    {
        if (obj is bool[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToBool()).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to bool array.");
    }

    public static string[] ToStringArray(this object obj)
    {
        if (obj is string[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to string array.");
    }

    public static Guid[] ToGuidArray(this object obj)
    {
        if (obj is Guid[] arr) return arr;
        if (obj is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(x => x.ToGuid() ?? Guid.Empty).ToArray();
        }
        throw new InvalidCastException($"Cannot convert {obj?.GetType()} to Guid array.");
    }
}
