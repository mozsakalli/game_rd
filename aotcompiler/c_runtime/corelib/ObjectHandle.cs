namespace System.Runtime.Remoting
{
    public class ObjectHandle
    {
        public object value;

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