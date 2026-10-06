// corelib: System.IO akis yuzeyi (bellek tabanli). Stream = sanal taban (abstract yerine
// NotSupported firlatan sanal uyeler); MemoryStream tek gercekleme (dosya akisi yok: motor
// dosyayi NativeFs/pak uzerinden byte[] olarak okur). BinaryReader/Writer .NET bicimiyle
// BIREBIR (little-endian; string = 7-bit uzunluk + UTF-8) — editorun BinaryWriter'la yazdigi
// artifact'ler release'te bu okuyucuyla acilir.
namespace System.IO
{
    enum SeekOrigin { Begin = 0, Current = 1, End = 2 }

    class EndOfStreamException : Exception
    {
        public EndOfStreamException() : base("Unable to read beyond the end of the stream.") { }
    }

    class Stream : IDisposable
    {
        public virtual bool CanRead { get { return false; } }
        public virtual bool CanWrite { get { return false; } }
        public virtual bool CanSeek { get { return false; } }
        public virtual long Length { get { throw new NotSupportedException(); } }
        public virtual long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public virtual int ReadByte() { throw new NotSupportedException(); }
        public virtual int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public virtual void WriteByte(byte value) { throw new NotSupportedException(); }
        public virtual void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public virtual long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public virtual void SetLength(long value) { throw new NotSupportedException(); }
        public virtual void Flush() { }
        public virtual void Close() { }
        public void Dispose() { Close(); }
        public void CopyTo(Stream destination)
        {
            byte[] buf = new byte[4096];
            int n;
            while ((n = Read(buf, 0, buf.Length)) > 0) destination.Write(buf, 0, n);
        }
    }

    class MemoryStream : Stream
    {
        byte[] buffer;
        int position;
        int length;
        bool expandable;

        public MemoryStream() { buffer = new byte[256]; expandable = true; }
        public MemoryStream(int capacity) { buffer = new byte[capacity < 16 ? 16 : capacity]; expandable = true; }
        public MemoryStream(byte[] buffer) { this.buffer = buffer; length = buffer.Length; expandable = false; }
        public MemoryStream(byte[] buffer, int index, int count)
        {
            this.buffer = new byte[count];
            Array.Copy(buffer, index, this.buffer, 0, count);
            length = count;
            expandable = false;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override long Length { get { return length; } }
        public override long Position
        {
            get { return position; }
            set { if (value < 0) throw new IOException(); position = (int)value; }
        }
        public int Capacity { get { return buffer.Length; } }

        void EnsureCapacity(int needed)
        {
            if (needed <= buffer.Length) return;
            if (!expandable) throw new NotSupportedException();
            int cap = buffer.Length * 2;
            if (cap < needed) cap = needed;
            byte[] n = new byte[cap];
            Array.Copy(buffer, 0, n, 0, length);
            buffer = n;
        }

        public override int ReadByte()
        {
            if (position >= length) return -1;
            return buffer[position++];
        }
        public override int Read(byte[] dst, int offset, int count)
        {
            int n = length - position;
            if (n > count) n = count;
            if (n <= 0) return 0;
            Array.Copy(buffer, position, dst, offset, n);
            position += n;
            return n;
        }
        public override void WriteByte(byte value)
        {
            EnsureCapacity(position + 1);
            buffer[position++] = value;
            if (position > length) length = position;
        }
        public override void Write(byte[] src, int offset, int count)
        {
            EnsureCapacity(position + count);
            Array.Copy(src, offset, buffer, position, count);
            position += count;
            if (position > length) length = position;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long p = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? position + offset : length + offset;
            if (p < 0) throw new IOException();
            position = (int)p;
            return p;
        }
        public override void SetLength(long value)
        {
            EnsureCapacity((int)value);
            length = (int)value;
            if (position > length) position = length;
        }
        public byte[] ToArray()
        {
            byte[] r = new byte[length];
            Array.Copy(buffer, 0, r, 0, length);
            return r;
        }
        public byte[] GetBuffer() { return buffer; }
    }

    class BinaryReader : IDisposable
    {
        Stream stream;
        public BinaryReader(Stream input) { stream = input; }
        public Stream BaseStream { get { return stream; } }
        public void Dispose() { stream.Close(); }
        public void Close() { stream.Close(); }

        int Next()
        {
            int b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            return b;
        }
        public byte ReadByte() { return (byte)Next(); }
        public sbyte ReadSByte() { return (sbyte)Next(); }
        public bool ReadBoolean() { return Next() != 0; }
        public char ReadChar() { return (char)ReadUInt16(); } // BMP varsayimi (motor metni UTF-8 string olarak tasir)
        public short ReadInt16() { int a = Next(); int b = Next(); return (short)(a | (b << 8)); }
        public ushort ReadUInt16() { int a = Next(); int b = Next(); return (ushort)(a | (b << 8)); }
        public int ReadInt32() { int a = Next(); int b = Next(); int c = Next(); int d = Next(); return a | (b << 8) | (c << 16) | (d << 24); }
        public uint ReadUInt32() { return (uint)ReadInt32(); }
        public long ReadInt64() { uint lo = ReadUInt32(); uint hi = ReadUInt32(); return (long)(((ulong)hi << 32) | lo); }
        public ulong ReadUInt64() { return (ulong)ReadInt64(); }
        public float ReadSingle() { return BitConverter.Int32BitsToSingle(ReadInt32()); }
        public double ReadDouble() { return BitConverter.Int64BitsToDouble(ReadInt64()); }
        public byte[] ReadBytes(int count)
        {
            byte[] r = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(r, got, count - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < count)
            {
                byte[] t = new byte[got];
                Array.Copy(r, 0, t, 0, got);
                return t;
            }
            return r;
        }
        public int Read7BitEncodedInt()
        {
            int result = 0, shift = 0;
            while (true)
            {
                int b = Next();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 35) throw new IOException();
            }
        }
        public string ReadString()
        {
            int len = Read7BitEncodedInt();
            if (len == 0) return "";
            byte[] bytes = ReadBytes(len);
            if (bytes.Length < len) throw new EndOfStreamException();
            return System.Text.Encoding.UTF8.GetString(bytes, 0, len);
        }
    }

    class BinaryWriter : IDisposable
    {
        Stream stream;
        public BinaryWriter(Stream output) { stream = output; }
        public Stream BaseStream { get { return stream; } }
        public void Flush() { stream.Flush(); }
        public void Dispose() { stream.Flush(); stream.Close(); }
        public void Close() { stream.Flush(); stream.Close(); }

        public void Write(byte value) { stream.WriteByte(value); }
        public void Write(sbyte value) { stream.WriteByte((byte)value); }
        public void Write(bool value) { stream.WriteByte(value ? (byte)1 : (byte)0); }
        public void Write(short value) { stream.WriteByte((byte)value); stream.WriteByte((byte)(value >> 8)); }
        public void Write(ushort value) { stream.WriteByte((byte)value); stream.WriteByte((byte)(value >> 8)); }
        public void Write(char value) { Write((ushort)value); }
        public void Write(int value)
        {
            stream.WriteByte((byte)value); stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 24));
        }
        public void Write(uint value) { Write((int)value); }
        public void Write(long value) { Write((int)value); Write((int)(value >> 32)); }
        public void Write(ulong value) { Write((long)value); }
        public void Write(float value) { Write(BitConverter.SingleToInt32Bits(value)); }
        public void Write(double value) { Write(BitConverter.DoubleToInt64Bits(value)); }
        public void Write(byte[] buffer) { stream.Write(buffer, 0, buffer.Length); }
        public void Write(byte[] buffer, int index, int count) { stream.Write(buffer, index, count); }
        public void Write7BitEncodedInt(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80) { stream.WriteByte((byte)(v | 0x80)); v >>= 7; }
            stream.WriteByte((byte)v);
        }
        public void Write(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Write7BitEncodedInt(bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
