using System;
namespace Demo16
{
    class Item
    {
        int id;
        public Item(int i) { id = i; }
        public override string ToString() { return "Item#" + id; }
    }
    class App16
    {
        public static int Run()
        {
            string a = "Merhaba";
            string b = "Mer" + "haba";
            int acc = 0;
            if (a == b) { acc = acc + 1; }
            string c2 = "Merhaba";
            object oa = a;
            object oc = c2;
            if (oa == oc) { acc = acc + 2; }
            object ob = b;
            if (oa == ob) { acc = acc + 1000; }
            string rc = a.Substring(0, 3) + a.Substring(3);
            if (rc == a) { acc = acc + 200; }
            acc = acc + a.Length;
            acc = acc + a.Substring(3).Length;
            acc = acc + a.Substring(1, 2).Length;
            acc = acc + a.IndexOf("haba");
            acc = acc + a.IndexOf("yok");
            if (a[1] == 'e') { acc = acc + 5; }
            string s2 = "ab" + 7;
            acc = acc + s2.Length;
            string s3 = 5 + "x" + 'q';
            acc = acc + s3.Length;
            if (a.GetHashCode() == b.GetHashCode()) { acc = acc + 10; }
            Item it = new Item(42);
            string ts = it.ToString();
            acc = acc + ts.Length;
            object o = it;
            if (o.ToString() == "Item#42") { acc = acc + 100; }
            if (a is string) { acc = acc + 4; }
            object os = a;
            string back = os as string;
            acc = acc + back.Length;
            if (a.Equals(c2)) { acc = acc + 20; }
            return acc;
        }
        static int Hello()
        {
            Console.WriteLine("Merhaba " + 42 + '!');
            Console.Write("x=");
            Console.Write(7);
            Console.WriteLine();
            Console.WriteLine(99);
            return 1;
        }
    }
}
