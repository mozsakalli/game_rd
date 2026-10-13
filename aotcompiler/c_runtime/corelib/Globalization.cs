namespace System
{
    // Bicimleme saglayicisi: kultur yok, hicbir API icerigini okumaz; yalniz C# yuzey uyumu (string.Format(provider, ...)).
    public interface IFormatProvider
    {
        public object GetFormat(Type formatType);
    }
}
namespace System.Globalization
{
    public enum NumberStyles
    {
        HexNumber = 515
    }
}