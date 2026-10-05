using System;
namespace Demo22 {
    class Animal22 { }
    class Dog22 : Animal22 { }
    class App22 {
        public static int Run() {
            int acc = 0;
            Type t = typeof(Dog22);
            if (t.FullName == "Demo22.Dog22") { acc += 1; }
            if (t.Name == "Dog22") { acc += 2; }
            if (t.BaseType == typeof(Animal22)) { acc += 4; }
            object o = new Dog22();
            if (o.GetType() == t) { acc += 8; }
            Animal22 a = o as Animal22;
            if (a.GetType() == typeof(Dog22)) { acc += 16; }
            if (typeof(string).FullName == "System.String") { acc += 32; }
            if (typeof(object).Name == "Object") { acc += 64; }
            if (typeof(Animal22).BaseType == typeof(object)) { acc += 128; }
            Type tt = t;
            if (tt == t) { acc += 256; }
            if (typeof(int).FullName == "System.Int32") { acc += 512; }
            if (typeof(int).Name == "Int32") { acc += 1024; }
            if (typeof(int) == typeof(int)) { acc += 2048; }
            if (typeof(int).BaseType.FullName == "System.ValueType") { acc += 4096; }
            if (typeof(int).BaseType.BaseType == typeof(object)) { acc += 8192; }
            if (typeof(float).Name == "Single") { acc += 16384; }
            if (typeof(bool).FullName == "System.Boolean") { acc += 32768; }
            return acc;
        }
    }
}
