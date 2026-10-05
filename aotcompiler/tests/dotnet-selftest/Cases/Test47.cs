using System;
using System.Threading.Tasks;
using System.Text;
using System.Globalization;
using static Demo47.StaticTypes47;
namespace Demo47
{
    class StaticTypes47
    {
        public class Nested47 { public int Value() { return 2; } }
    }
    class LexicalThis47
    {
        int value = 4;
        Action changed;
        public int Read()
        {
            int result = 0;
            Action outer = () => { Action inner = () => { changed += () => result++; result = value; }; inner(); };
            outer();
            changed();
            return result;
        }
    }
    class Shadow47
    {
        public static int Value { get { return 4; } }
    }
    class ArrayObject47
    {
        public static int Take(object value) { return value != null ? 8 : 0; }
    }
    class SwitchThrow47
    {
        public static int Read(int value)
        {
            switch (value)
            {
                case 1: return 128;
                default: throw new Exception("bad");
            }
        }
    }
    class Constraint47
    {
        public static T Cast<T>(object value) where T : class { return value as T; }
    }
    class GenericType47<T> { }
    enum TestEnum47 { Seven = 7 }
    class Enclosing47
    {
        static int AddOne(int value) { return value + 1; }
        public class Nested { public int Read() { return AddOne(6); } }
    }
    class Increment47
    {
        public int Value { get; set; }
        public int Run() { Value++; return ++Value; }
        public static int RefRun(ref int value) { int old = value++; return old + ++value; }
    }
    class Bound47 { public int Value = 7; }
    class SortComparer47 : System.Collections.Generic.IComparer<Bound47>
    {
        public int Compare(Bound47 left, Bound47 right) { return left.Value - right.Value; }
    }
    class BoundHolder47<T> where T : Bound47
    {
        public int Read(T value) { return value.Value; }
        public static bool Is<U>(Bound47 value) where U : Bound47 { return value is U; }
        public static U As<U>(Bound47 value) where U : Bound47 { return value as U; }
    }
    class NullCall47
    {
        public Created47 Find(int value) { return value == 1 ? new Created47() : null; }
        public static bool Run()
        {
            NullCall47 missing = null;
            return missing?.Find(1) == null && new NullCall47()?.Find(1) != null;
        }
    }
    class LegacyEnumerator47 : System.Collections.IEnumerator
    {
        int index = -1;
        public bool MoveNext() { index++; return index < 2; }
        public object Current { get { return index == 0 ? "a" : "b"; } }
        public void Reset() { index = -1; }
    }
    class LegacyList47 : System.Collections.IList
    {
        public int Count { get { return 2; } }
        public object this[int index] { get { return index == 0 ? "a" : "b"; } set { } }
        public System.Collections.IEnumerator GetEnumerator() { return new LegacyEnumerator47(); }
        public int Add(object value) { return -1; }
        public void Clear() { }
        public bool Contains(object value) { return false; }
        public int IndexOf(object value) { return -1; }
        public void Insert(int index, object value) { }
        public void Remove(object value) { }
        public void RemoveAt(int index) { }
        public bool IsFixedSize { get { return true; } }
        public bool IsReadOnly { get { return true; } }
        public void CopyTo(System.Array array, int index) { }
        public bool IsSynchronized { get { return false; } }
        public object SyncRoot { get { return this; } }
    }
    class Created47
    {
        int value;
        public Created47() { value = 40; }
        public int Result() { return value; }
    }
    struct RefValue47 { public int Value; }
    class RefObject47 { public int Value; }
    class RefHolder47 { public RefValue47 Value; public RefObject47 Object; }
    unsafe class App47
    {
        static Func<int, int> staticLambda = value => value + 5;
        Shadow47 Shadow47;
        static int nullSeen;
        static object gate = new object();
        static int lockTargetCount;
        static object LockTarget() { lockTargetCount++; return gate; }
        static int LockReturn() { lock (LockTarget()) { return 128; } }
        static int LockAgain() { lock (gate) { return 256; } }
        static void SeeNull(string text, object value) { if (value == null) nullSeen = 8; }
        static Created47 Create<T>() { return Activator.CreateInstance(typeof(T)) as Created47; }
        static T Cast<T>(object value) { return (T)value; }
        static bool IsBytes<T>() { return typeof(T) == typeof(byte[]); }
        static int Add(int name, int sender, int args) { return name + sender + args; }
        static int Captured(int name, int sender, int args)
        {
            Func<int> callback = delegate () { return Add(name, sender, args); };
            return callback.Invoke();
        }
        static int MutableCapture()
        {
            int value = 1;
            Action change = delegate () { value += 2; value++; };
            value += 4;
            change();
            return value;
        }
        static int NestedCapture()
        {
            int value = 1;
            Action outer = delegate ()
            {
                Action inner = delegate () { value += 2; };
                inner();
            };
            outer();
            return value;
        }
        static void SetRefValue(ref RefValue47 value) { value.Value = 17; }
        static void SetRefObject(ref RefObject47 value) { value = new RefObject47(); value.Value = 23; }
        static void SetRefInt(ref int value) { value = 29; }
        static int StringSwitch(string value)
        {
            switch (value)
            {
                case "first": return 31;
                case "second": return 37;
                default: return 41;
            }
        }
        static bool MarshalRoundTrip()
        {
            var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(2);
            byte* bytes = (byte*)memory;
            var alias = bytes;
            bytes[0] = 43;
            alias += 1;
            alias[0] = 47;
            bool result = bytes[0] == 43 && alias[0] == 47;
            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)bytes);
            return result;
        }
        public static int Run()
        {
            int hex;
            int legacyCount = 0;
            object nil = null;
            var inner = new Exception("inner");
            var outer = new Exception("outer", inner);
            var regexMatch = System.Text.RegularExpressions.Regex.Match("x{locale:key|1}y", "\\{locale:([^\\}]+)\\}");
            var regexReplace = System.Text.RegularExpressions.Regex.Replace("aTexture_Pixel(uv)b", "Texture_Pixel\\(([^\\)]+)\\)", m => "[" + m.Groups[1].Value + "]");
            float[] copySource = { 1, 2 };
            float[] copyTarget = new float[2];
            Array.Copy(copySource, copyTarget, 2);
            int[] overlap = { 1, 2, 3 };
            Array.Copy(overlap, 0, overlap, 1, 2);
            var splitAll = "a::b".Split(':');
            var splitTwo = "a:b:c".Split(":", 2);
            Func<object, object> boxedIdentity = value => value;
            int intStringValue = -12;
            uint uintStringValue = 7;
            long longStringValue = 5000000000L;
            float floatStringValue = 2.5f;
            double doubleStringValue = 3.5;
            TestEnum47 parsedEnum;
            bool parsedEnumOk = Enum.TryParse("Seven", out parsedEnum);
            TestEnum47 missingEnum;
            bool missingEnumOk = Enum.TryParse("Missing", out missingEnum);
            int incrementValue = 3;
            int refIncrement = Increment47.RefRun(ref incrementValue);
            uint uncheckedValue = unchecked((uint)-1);
            var refHolder = new RefHolder47();
            SetRefValue(ref refHolder.Value);
            SetRefObject(ref refHolder.Object);
            int[] refArray = { 0 };
            SetRefInt(ref refArray[0]);
            var findList = new System.Collections.Generic.List<RefObject47>();
            findList.Add(new RefObject47 { Value = 3 });
            findList.Add(refHolder.Object);
            var foundObject = findList.Find(value => value.Value == 23);
            var missingObject = findList.Find(value => value.Value == 99);
            var foundArray = findList.ToArray();
            var rangeList = new System.Collections.Generic.List<RefObject47>();
            rangeList.AddRange(findList);
            rangeList.AddRange(rangeList);
            Bound47[] sorted = { new Bound47 { Value = 3 }, new Bound47 { Value = 1 }, new Bound47 { Value = 2 } };
            System.Collections.Generic.IComparer<Bound47> sortComparer = new SortComparer47();
            Array.Sort<Bound47>(sorted, 0, sorted.Length, sortComparer);
            System.Collections.IList legacy = new LegacyList47();
            foreach (object item in legacy) legacyCount++;
            var genericList = new System.Collections.Generic.List<int>();
            genericList.Add(2);
            genericList.Add(3);
            System.Collections.Generic.IEnumerable<int> genericSequence = genericList;
            int genericSum = 0;
            foreach (int item in genericSequence) genericSum += item;
            var handle = Activator.CreateInstance(null, "Demo47.Created47");
            Created47 created = handle.Unwrap() as Created47;
            Created47 genericCreated = Create<Created47>();
            Created47 genericCreatedNoArg = Activator.CreateInstance<Created47>();
            var missing = Activator.CreateInstance(null, "Demo47.Missing47");
            Action<string, object> nullableCallback = SeeNull;
            nullableCallback("x", null);
            return created.Result() + genericCreated.Result() + genericCreatedNoArg.Result() + Cast<Created47>(created).Result()
                + Cast<int>((object)7) + (missing == null ? 2 : 0) + Captured(1, 2, 3) + MutableCapture()
                + NestedCapture()
                + Shadow47.Value + ((object)new byte[1] != null ? 2 : 0) + nullSeen
                + (IsBytes<byte[]>() ? 16 : 0) + (!IsBytes<int[]>() ? 32 : 0)
                + (Uri.EscapeDataString("a b&ç") == "a%20b%26%C3%A7" ? 64 : 0)
                + LockReturn() + LockAgain() + (lockTargetCount == 1 ? 512 : 0)
                + (new StringBuilder().Append("a").Append("b").ToString() == "ab" ? 1024 : 0)
                + new Nested47().Value() + new LexicalThis47().Read()
                + ArrayObject47.Take(new byte[1])
                + ($"{true}/{false}" == "True/False" ? 16 : 0)
                + (uint.MaxValue == 4294967295u ? 32 : 0)
                + ("a--b--".Replace("--", "+").Replace('+', ':') == "a:b:" ? 64 : 0)
                + SwitchThrow47.Read(1)
                + (MathF.Min(64, 3) == 3 ? 256 : 0)
                + (Convert.ToInt32("20ac", 16) == 8364 && Convert.ToUInt32(7) == 7u ? 512 : 0)
                + (int.TryParse("20AC", NumberStyles.HexNumber, null, out hex) && hex == 8364 ? 1024 : 0)
                + (Constraint47.Cast<Created47>(created) != null ? 2048 : 0)
                + (typeof(GenericType47<Created47>).GetGenericArguments()[0] == typeof(Created47) ? 4096 : 0)
                + (legacyCount == 2 && genericSum == 5 ? 8192 : 0)
                + (typeof(TypeCode).IsEnum && !typeof(Created47).IsEnum ? 16384 : 0)
                + ((TestEnum47)Enum.ToObject(typeof(TestEnum47), (object)7) == TestEnum47.Seven && parsedEnumOk && parsedEnum == TestEnum47.Seven && !missingEnumOk ? 32768 : 0)
                + (nil is null && !(created is null) ? 65536 : 0)
                + (outer.Message == "outer" && outer.InnerException == inner ? 131072 : 0)
                + (int.MaxValue == 2147483647 && int.MinValue == -2147483647 - 1 ? 262144 : 0)
                + (" \tab c\r\n ".Trim() == "ab c" && "Abc".ToUpper() == "ABC" && "xy".ToCharArray()[1] == 'y' ? 524288 : 0)
                + (regexMatch.Success && regexMatch.Index == 1 && regexMatch.Groups[1].Value == "key|1" && regexReplace == "a[uv]b" ? 1048576 : 0)
                + (Console.Error != null && Console.Error == Console.Error ? 2097152 : 0)
                + (copyTarget[0] == 1 && copyTarget[1] == 2 && overlap[1] == 1 && overlap[2] == 2 ? 4194304 : 0)
                + (1.5f + "x" == "1.5x" ? 8388608 : 0)
                + (splitAll.Length == 3 && splitAll[1] == "" && splitAll[2] == "b" && splitTwo.Length == 2 && splitTwo[1] == "b:c" ? 16777216 : 0)
                + ((long)boxedIdentity(9L) == 9 ? 33554432 : 0)
                + (intStringValue.ToString() == "-12" && uintStringValue.ToString() == "7" && longStringValue.ToString() == "5000000000" && floatStringValue.ToString() == "2.5" && doubleStringValue.ToString() == "3.5" && float.IsPositiveInfinity(float.PositiveInfinity) && !float.IsPositiveInfinity(1.0f) && double.MinValue < -1.0 ? 67108864 : 0)
                + (refHolder.Value.Value == 17 && refHolder.Object.Value == 23 && refArray[0] == 29 ? 134217728 : 0)
                + (true || false ? 268435456 : 0)
                + (foundObject == refHolder.Object && missingObject == null && foundArray.Length == 2 && foundArray[1] == refHolder.Object && rangeList.Count == 4 && rangeList[3] == refHolder.Object && sorted[0].Value == 1 && sorted[1].Value == 2 && sorted[2].Value == 3 && StringSwitch("second") == 37 && StringSwitch("other") == 41 && typeof(int?) != typeof(int) && typeof(int?).GetGenericArguments()[0] == typeof(int) && typeof(RefValue47) != typeof(RefHolder47) && staticLambda(7) == 12 && new Enclosing47.Nested().Read() == 7 && "Ab".Contains('b') && "Ab".ToLowerInvariant() == "ab" && object.Equals(created, created) && !object.Equals(created, null) && MathF.Atan2(0, 1) == 0 && float.MaxValue > 3E+38f && float.PositiveInfinity > float.MaxValue && double.MaxValue > 1E+308 && short.MaxValue == 32767 && new Increment47().Run() == 2 && refIncrement == 8 && incrementValue == 5 && uncheckedValue == uint.MaxValue && new BoundHolder47<Bound47>().Read(new Bound47()) == 7 && BoundHolder47<Bound47>.Is<Bound47>(new Bound47()) && BoundHolder47<Bound47>.As<Bound47>(new Bound47()) != null && NullCall47.Run() && MarshalRoundTrip() ? 536870912 : 0);
        }
    }
}
