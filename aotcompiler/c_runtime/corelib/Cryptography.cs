// Desktop crypto is a host contract. The native backend is intentionally required at link
// time when AES is used; there is no managed fallback or no-op implementation.
namespace System.Security.Cryptography
{
    public enum CipherMode { CBC = 1 }
    public enum PaddingMode { PKCS7 = 2 }

    public interface ICryptoTransform
    {
        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount);
    }

    public class RijndaelManaged
    {
        public CipherMode Mode { get; set; }
        public PaddingMode Padding { get; set; }
        public int KeySize { get; set; }
        public int BlockSize { get; set; }
        public byte[] Key { get; set; }
        public byte[] IV { get; set; }

        public extern ICryptoTransform CreateEncryptor();
        public extern ICryptoTransform CreateDecryptor();
    }

    // ---- SHA-256 / HMAC: saf C# (FIPS 180-4, RFC 2104). Hot path degil: cagri basina kucuk alloc kabul. ----
    public abstract class HashAlgorithm : IDisposable
    {
        public abstract int HashSize { get; }
        public byte[] ComputeHash(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            return ComputeHash(buffer, 0, buffer.Length);
        }
        public byte[] ComputeHash(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException("count");
            return HashCore(buffer, offset, count);
        }
        protected abstract byte[] HashCore(byte[] data, int offset, int count);
        public virtual void Dispose() { }
    }

    public class SHA256 : HashAlgorithm
    {
        static readonly uint[] K =
        {
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
            0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
            0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
            0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
            0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
            0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
        };

        public override int HashSize { get { return 256; } }
        public static SHA256 Create() { return new SHA256(); }
        public static byte[] HashData(byte[] source) { return Compute(source, 0, source.Length); }
        protected override byte[] HashCore(byte[] data, int offset, int count) { return Compute(data, offset, count); }

        static uint Rotr(uint x, int n) { return (x >> n) | (x << (32 - n)); }

        internal static byte[] Compute(byte[] data, int offset, int count)
        {
            // dolgu: 0x80 + sifirlar + 64 bit big-endian uzunluk; toplam 64'un kati
            int padded = ((count + 8) / 64 + 1) * 64;
            var msg = new byte[padded];
            for (int i = 0; i < count; i++) msg[i] = data[offset + i];
            msg[count] = 0x80;
            long bits = (long)count * 8;
            for (int i = 0; i < 8; i++) msg[padded - 1 - i] = (byte)(bits >> (8 * i));

            uint h0 = 0x6a09e667u, h1 = 0xbb67ae85u, h2 = 0x3c6ef372u, h3 = 0xa54ff53au;
            uint h4 = 0x510e527fu, h5 = 0x9b05688cu, h6 = 0x1f83d9abu, h7 = 0x5be0cd19u;
            var w = new uint[64];
            for (int block = 0; block < padded; block += 64)
            {
                for (int t = 0; t < 16; t++)
                {
                    int j = block + t * 4;
                    w[t] = ((uint)msg[j] << 24) | ((uint)msg[j + 1] << 16) | ((uint)msg[j + 2] << 8) | msg[j + 3];
                }
                for (int t = 16; t < 64; t++)
                {
                    uint s0 = Rotr(w[t - 15], 7) ^ Rotr(w[t - 15], 18) ^ (w[t - 15] >> 3);
                    uint s1 = Rotr(w[t - 2], 17) ^ Rotr(w[t - 2], 19) ^ (w[t - 2] >> 10);
                    w[t] = w[t - 16] + s0 + w[t - 7] + s1;
                }
                uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
                for (int t = 0; t < 64; t++)
                {
                    uint S1 = Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25);
                    uint ch = (e & f) ^ (~e & g);
                    uint t1 = h + S1 + ch + K[t] + w[t];
                    uint S0 = Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22);
                    uint maj = (a & b) ^ (a & c) ^ (b & c);
                    uint t2 = S0 + maj;
                    h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }
                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h;
            }
            var r = new byte[32];
            Put(r, 0, h0); Put(r, 4, h1); Put(r, 8, h2); Put(r, 12, h3); Put(r, 16, h4); Put(r, 20, h5); Put(r, 24, h6); Put(r, 28, h7);
            return r;
        }
        static void Put(byte[] r, int i, uint v) { r[i] = (byte)(v >> 24); r[i + 1] = (byte)(v >> 16); r[i + 2] = (byte)(v >> 8); r[i + 3] = (byte)v; }
    }

    public abstract class HMAC : HashAlgorithm
    {
        byte[] _key;
        public virtual byte[] Key
        {
            get { var c = new byte[_key.Length]; for (int i = 0; i < c.Length; i++) c[i] = _key[i]; return c; }
            set { if (value == null) throw new ArgumentNullException("value"); _key = new byte[value.Length]; for (int i = 0; i < value.Length; i++) _key[i] = value[i]; }
        }
        protected byte[] RawKey { get { return _key; } }
        public override void Dispose() { if (_key != null) for (int i = 0; i < _key.Length; i++) _key[i] = 0; }
    }

    public class HMACSHA256 : HMAC
    {
        const int BlockSize = 64;
        public HMACSHA256(byte[] key) { Key = key; }
        public override int HashSize { get { return 256; } }
        public static byte[] HashData(byte[] key, byte[] source) { using (var h = new HMACSHA256(key)) return h.ComputeHash(source); }

        protected override byte[] HashCore(byte[] data, int offset, int count)
        {
            byte[] key = RawKey;
            if (key.Length > BlockSize) key = SHA256.Compute(key, 0, key.Length);
            var inner = new byte[BlockSize + count];
            var outerKey = new byte[BlockSize];
            for (int i = 0; i < BlockSize; i++)
            {
                byte k = i < key.Length ? key[i] : (byte)0;
                inner[i] = (byte)(k ^ 0x36);
                outerKey[i] = (byte)(k ^ 0x5c);
            }
            for (int i = 0; i < count; i++) inner[BlockSize + i] = data[offset + i];
            byte[] innerHash = SHA256.Compute(inner, 0, inner.Length);
            var outer = new byte[BlockSize + innerHash.Length];
            for (int i = 0; i < BlockSize; i++) outer[i] = outerKey[i];
            for (int i = 0; i < innerHash.Length; i++) outer[BlockSize + i] = innerHash[i];
            return SHA256.Compute(outer, 0, outer.Length);
        }
    }
}