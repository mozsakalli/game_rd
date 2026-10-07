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
}