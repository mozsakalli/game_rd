namespace System.Text
{
    class StringBuilder
    {
        string value = "";

        public StringBuilder Append(string item)
        {
            value += item;
            return this;
        }

        public override string ToString()
        {
            return value;
        }
    }
}