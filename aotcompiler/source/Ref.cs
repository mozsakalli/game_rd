namespace DigitoyEngine.Language
{
    // AddrLocal/AddrArg'in urettigi "adres": bir object[] icindeki tek slotu isaret eder.
    // LoadInd/StoreInd bu adres uzerinden okur/yazar (CIL ldind/stind, ref/out parametre gecisi icin).
    public class Ref
    {
        readonly object[] storage;
        readonly int index;
        public Ref(object[] storage, int index) { this.storage = storage; this.index = index; }
        public object Get() => storage[index];
        public void Set(object value) => storage[index] = value;
    }
}
