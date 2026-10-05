namespace System.Runtime.Remoting
{
    class ObjectHandle
    {
        object value;

        public ObjectHandle(object value)
        {
            this.value = value;
        }

        public object Unwrap()
        {
            return value;
        }
    }
}