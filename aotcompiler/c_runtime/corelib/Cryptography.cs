// Desktop crypto is a host contract. The native backend is intentionally required at link
// time when AES is used; there is no managed fallback or no-op implementation.
namespace System.Security.Cryptography
{
    enum CipherMode { CBC = 1 }
    enum PaddingMode { PKCS7 = 2 }

    interface ICryptoTransform
    {
        byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount);
    }

    class RijndaelManaged
    {
        public CipherMode Mode { get; set; }
        public PaddingMode Padding { get; set; }
        public int KeySize { get; set; }
        public int BlockSize { get; set; }
        public byte[] Key { get; set; }
        public byte[] IV { get; set; }

        extern public ICryptoTransform CreateEncryptor();
        extern public ICryptoTransform CreateDecryptor();
    }
}