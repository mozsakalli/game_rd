namespace System.Text
{
    class StringBuilder
    {
        string value = "";

        public StringBuilder() { }
        public StringBuilder(int capacity) { } // kapasite ipucu: bu basit gerceklemede anlamsiz

        public int Length { get { return value.Length; } }

        public StringBuilder Append(string item)
        {
            value += item;
            return this;
        }

        public StringBuilder Append(char item)
        {
            value += item;
            return this;
        }

        public StringBuilder Append(char item, int repeatCount)
        {
            for (int i = 0; i < repeatCount; i++) value += item;
            return this;
        }

        public StringBuilder Append(int item)
        {
            value += item;
            return this;
        }

        public StringBuilder Append(float item)
        {
            value += item;
            return this;
        }

        public StringBuilder Append(bool item)
        {
            value += item;
            return this;
        }

        public StringBuilder Clear()
        {
            value = "";
            return this;
        }

        public override string ToString()
        {
            return value;
        }
    }
}