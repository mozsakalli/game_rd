namespace System.Text
{
    // Gercek tampon gerceklemesi (BCL gibi): char[] + uzunluk, ikiye katlayarak buyume. Eski surum `string value += item`
    // idi -> her Append tum metni kopyaliyordu (O(n^2)); 180 KB Base64 tek frame'de 6+ sn donduruyordu (ANR watchdog yakaladi).
    public class StringBuilder
    {
        private const int DefaultCapacity = 16;
        private char[] buf;
        private int len;

        public StringBuilder() { buf = new char[DefaultCapacity]; }
        public StringBuilder(int capacity) { buf = new char[capacity > 0 ? capacity : DefaultCapacity]; }
        public StringBuilder(string value)
        {
            int n = value == null ? 0 : value.Length;
            buf = new char[n > DefaultCapacity ? n : DefaultCapacity];
            if (n > 0) Append(value);
        }

        public int Capacity { get { return buf.Length; } }

        public int Length
        {
            get { return len; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException("value");
                if (value > len) { Ensure(value - len); for (int i = len; i < value; i++) buf[i] = '\0'; } // BCL: '\0' ile doldurur
                len = value;
            }
        }

        public char this[int index]
        {
            get { if ((uint)index >= (uint)len) throw new IndexOutOfRangeException(); return buf[index]; }
            set { if ((uint)index >= (uint)len) throw new ArgumentOutOfRangeException("index"); buf[index] = value; }
        }

        // len+extra sigacak sekilde buyut: en az iki kat (amortize O(1) ekleme). Kopya C memmove (Array non-generic Copy).
        private void Ensure(int extra)
        {
            int need = len + extra;
            if (need <= buf.Length) return;
            int cap = buf.Length * 2;
            if (cap < need) cap = need;
            var nb = new char[cap];
            if (len > 0) Array.Copy((Array)buf, 0, (Array)nb, 0, len);
            buf = nb;
        }

        public StringBuilder Append(char item)
        {
            if (len == buf.Length) Ensure(1);
            buf[len++] = item;
            return this;
        }

        public StringBuilder Append(string item)
        {
            if (item == null) return this;
            int n = item.Length;
            if (n == 0) return this;
            Ensure(n);
            if (n <= 8) { for (int i = 0; i < n; i++) buf[len + i] = item[i]; } // kisa: indexer dongusu (tahsissiz)
            else Array.Copy((Array)item.ToCharArray(), 0, (Array)buf, len, n);  // uzun: 1 tahsis + 2 memcpy
            len += n;
            return this;
        }

        public StringBuilder Append(string item, int startIndex, int count)
        {
            if (item == null)
            {
                if (startIndex == 0 && count == 0) return this;
                throw new ArgumentNullException("value");
            }
            if (startIndex < 0 || count < 0 || startIndex + count > item.Length) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return this;
            Ensure(count);
            if (count <= 8) { for (int i = 0; i < count; i++) buf[len + i] = item[startIndex + i]; }
            else Array.Copy((Array)item.ToCharArray(), startIndex, (Array)buf, len, count);
            len += count;
            return this;
        }

        public StringBuilder Append(char[] value)
        {
            if (value == null || value.Length == 0) return this;
            Ensure(value.Length);
            Array.Copy((Array)value, 0, (Array)buf, len, value.Length);
            len += value.Length;
            return this;
        }

        public StringBuilder Append(char[] value, int startIndex, int charCount)
        {
            if (value == null) { if (startIndex == 0 && charCount == 0) return this; throw new ArgumentNullException("value"); }
            if (startIndex < 0 || charCount < 0 || startIndex + charCount > value.Length) throw new ArgumentOutOfRangeException("charCount");
            if (charCount == 0) return this;
            Ensure(charCount);
            Array.Copy((Array)value, startIndex, (Array)buf, len, charCount);
            len += charCount;
            return this;
        }

        public StringBuilder Append(char item, int repeatCount)
        {
            if (repeatCount < 0) throw new ArgumentOutOfRangeException("repeatCount");
            Ensure(repeatCount);
            for (int i = 0; i < repeatCount; i++) buf[len + i] = item;
            len += repeatCount;
            return this;
        }

        public StringBuilder Append(object item) { return item == null ? this : Append(item.ToString()); }
        public StringBuilder Append(int item) { return Append(item.ToString()); }
        public StringBuilder Append(uint item) { return Append(item.ToString()); }
        public StringBuilder Append(long item) { return Append(item.ToString()); }
        public StringBuilder Append(float item) { return Append(item.ToString()); }
        public StringBuilder Append(double item) { return Append(item.ToString()); }
        public StringBuilder Append(bool item) { return Append(item.ToString()); }

        // Environment.NewLine corelib'de yok; tum hedeflerde '\n' (Android/iOS/wasm/Win ayni cikti).
        public StringBuilder AppendLine() { return Append('\n'); }
        public StringBuilder AppendLine(string value) { Append(value); return Append('\n'); }

        public StringBuilder Insert(int index, string value)
        {
            if ((uint)index > (uint)len) throw new ArgumentOutOfRangeException("index");
            if (value == null || value.Length == 0) return this;
            int n = value.Length;
            Ensure(n);
            if (index < len) Array.Copy((Array)buf, index, (Array)buf, index + n, len - index); // memmove: ortusme guvenli
            for (int i = 0; i < n; i++) buf[index + i] = value[i];
            len += n;
            return this;
        }

        public StringBuilder Insert(int index, char value)
        {
            if ((uint)index > (uint)len) throw new ArgumentOutOfRangeException("index");
            Ensure(1);
            if (index < len) Array.Copy((Array)buf, index, (Array)buf, index + 1, len - index);
            buf[index] = value;
            len++;
            return this;
        }

        public StringBuilder Remove(int startIndex, int length)
        {
            if (startIndex < 0 || length < 0 || startIndex + length > len) throw new ArgumentOutOfRangeException("length");
            if (length == 0) return this;
            int tail = len - (startIndex + length);
            if (tail > 0) Array.Copy((Array)buf, startIndex + length, (Array)buf, startIndex, tail);
            len -= length;
            return this;
        }

        public StringBuilder Clear()
        {
            len = 0;
            return this;
        }

        public override string ToString()
        {
            return len == 0 ? "" : new string(buf, 0, len);
        }

        public string ToString(int startIndex, int length)
        {
            if (startIndex < 0 || length < 0 || startIndex + length > len) throw new ArgumentOutOfRangeException("length");
            return length == 0 ? "" : new string(buf, startIndex, length);
        }
    }
}