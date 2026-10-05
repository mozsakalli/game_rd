// corelib: BitConverter (little-endian; hedef platformlarin tamami LE).
namespace System
{
    static class BitConverter
    {
        public static int ToInt32(byte[] value, int startIndex)
        {
            return value[startIndex] | (value[startIndex + 1] << 8) | (value[startIndex + 2] << 16) | (value[startIndex + 3] << 24);
        }
        public static uint ToUInt32(byte[] value, int startIndex) { return (uint)ToInt32(value, startIndex); }
        public static short ToInt16(byte[] value, int startIndex) { return (short)(value[startIndex] | (value[startIndex + 1] << 8)); }
        public static ushort ToUInt16(byte[] value, int startIndex) { return (ushort)(value[startIndex] | (value[startIndex + 1] << 8)); }
        public static float ToSingle(byte[] value, int startIndex) { return Int32BitsToSingle(ToInt32(value, startIndex)); }
        public static byte[] GetBytes(int value)
        {
            byte[] b = new byte[4];
            b[0] = (byte)value; b[1] = (byte)(value >> 8); b[2] = (byte)(value >> 16); b[3] = (byte)(value >> 24);
            return b;
        }
        public static byte[] GetBytes(float value) { return GetBytes(SingleToInt32Bits(value)); }
        extern public static int SingleToInt32Bits(float value);
        extern public static float Int32BitsToSingle(int value);
    }
}