// corelib: BitConverter (little-endian; hedef platformlarin tamami LE).
// Span<byte> hedefli TryWriteBytes: Span intrinsic temsili {adres, uzunluk} -> SpanOps ham yazim.
namespace System
{
    static class BitConverter
    {
        public static bool IsLittleEndian { get { return true; } }

        public static int ToInt32(byte[] value, int startIndex)
        {
            return value[startIndex] | (value[startIndex + 1] << 8) | (value[startIndex + 2] << 16) | (value[startIndex + 3] << 24);
        }
        public static uint ToUInt32(byte[] value, int startIndex) { return (uint)ToInt32(value, startIndex); }
        public static short ToInt16(byte[] value, int startIndex) { return (short)(value[startIndex] | (value[startIndex + 1] << 8)); }
        public static ushort ToUInt16(byte[] value, int startIndex) { return (ushort)(value[startIndex] | (value[startIndex + 1] << 8)); }
        public static long ToInt64(byte[] value, int startIndex)
        {
            uint lo = ToUInt32(value, startIndex);
            uint hi = ToUInt32(value, startIndex + 4);
            return (long)(((ulong)hi << 32) | lo);
        }
        public static ulong ToUInt64(byte[] value, int startIndex) { return (ulong)ToInt64(value, startIndex); }
        public static float ToSingle(byte[] value, int startIndex) { return Int32BitsToSingle(ToInt32(value, startIndex)); }
        public static double ToDouble(byte[] value, int startIndex) { return Int64BitsToDouble(ToInt64(value, startIndex)); }
        public static bool ToBoolean(byte[] value, int startIndex) { return value[startIndex] != 0; }

        public static byte[] GetBytes(short value)
        {
            byte[] b = new byte[2];
            b[0] = (byte)value; b[1] = (byte)(value >> 8);
            return b;
        }
        public static byte[] GetBytes(ushort value) { return GetBytes((short)value); }
        public static byte[] GetBytes(int value)
        {
            byte[] b = new byte[4];
            b[0] = (byte)value; b[1] = (byte)(value >> 8); b[2] = (byte)(value >> 16); b[3] = (byte)(value >> 24);
            return b;
        }
        public static byte[] GetBytes(uint value) { return GetBytes((int)value); }
        public static byte[] GetBytes(long value)
        {
            byte[] b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)(value >> (8 * i));
            return b;
        }
        public static byte[] GetBytes(ulong value) { return GetBytes((long)value); }
        public static byte[] GetBytes(float value) { return GetBytes(SingleToInt32Bits(value)); }
        public static byte[] GetBytes(double value) { return GetBytes(DoubleToInt64Bits(value)); }

        public static bool TryWriteBytes(Span<byte> destination, int value)
        {
            if (destination.Length < 4) return false;
            SpanOps.StoreInt32(destination.Ptr, value);
            return true;
        }
        public static bool TryWriteBytes(Span<byte> destination, short value)
        {
            if (destination.Length < 2) return false;
            SpanOps.StoreInt16(destination.Ptr, value);
            return true;
        }
        public static bool TryWriteBytes(Span<byte> destination, float value) { return TryWriteBytes(destination, SingleToInt32Bits(value)); }

        extern public static int SingleToInt32Bits(float value);
        extern public static float Int32BitsToSingle(int value);
        extern public static long DoubleToInt64Bits(double value);
        extern public static double Int64BitsToDouble(long value);
    }
}
