using System;
namespace Demo17
{
    class MyErr17 : Exception { public MyErr17(string m) : base(m) { } }
    class App17
    {
        int f;
        static int Boom() { throw new MyErr17("kaboom"); }
        static int Poke(App17 x) { return x.f; }
        public static int Run()
        {
            int acc = 0;
            try { acc = acc + Boom(); }
            catch (Exception e)
            {
                if (e.Message == "kaboom") { acc = acc + 1; }
                string st = e.StackTrace;
                if (st.IndexOf("Demo17.App17.Boom()") >= 0) { acc = acc + 2; }
                if (st.IndexOf("Demo17.App17.Run()") >= 0) { acc = acc + 4; }
            }
            try
            {
                int[] a = new int[2];
                acc = acc + a[5];
            }
            catch (IndexOutOfRangeException ex)
            {
                if (ex.Message == "Index was outside the bounds of the array.") { acc = acc + 8; }
            }
            try
            {
                App17 x = null;
                acc = acc + Poke(x);
            }
            catch (NullReferenceException nx)
            {
                if (nx.Message == "Object reference not set to an instance of an object.") { acc = acc + 16; }
            }
            Exception plain = new Exception("ozel mesaj");
            if (plain.Message == "ozel mesaj") { acc = acc + 32; }
            Exception dflt = new Exception();
            if (dflt.Message == "Exception of type 'System.Exception' was thrown.") { acc = acc + 64; }
            return acc;
        }
    }
}
