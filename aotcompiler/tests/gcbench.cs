// GC stres benchmark'i (MiniCs): oyun dongusu deseni - her frame bol gecici nesne, kalici
// dunya uzerinde old->young yazimlari (write barrier/remembered set), churn (tenured olum).
// Zamanlama el yazimi C main'dedir (gcbench_main.c); Frame donusu checksum'dur (DCE engeli).
using System;

namespace GcBench
{
    public class Vec
    {
        public float x;
        public float y;
        public float z;
        public Vec(float a, float b, float c) { x = a; y = b; z = c; }
    }

    public class Entity
    {
        public Vec pos;
        public Vec vel;
        public string name;
        public int hp;
    }

    public class Event
    {
        public int kind;
        public Vec at;
        public Event prev;
    }

    public class App
    {
        static Entity[] world; // kalici kok (static ref -> digitoyengine_statics_trace)
        static uint seed;

        static int Rnd(int n)
        {
            seed = seed * 1103515245u + 12345u; // uint: tasma tanimli (wrap)
            return (int)((seed >> 16) & 0x7FFFu) % n;
        }

        public static void Setup(int n)
        {
            seed = 12345u;
            world = new Entity[n];
            for (int i = 0; i < world.Length; i++)
            {
                Entity e = new Entity();
                e.pos = new Vec(i, i, i);
                e.vel = new Vec(1, 2, 3);
                e.name = "ent" + i;
                e.hp = 100;
                world[i] = e;
            }
        }

        public static int Frame(int f)
        {
            int acc = 0;
            // 1) her entity yeni pos Vec'i alir: eski Vec cop, tenured entity -> genc Vec yazimi (barrier)
            for (int i = 0; i < world.Length; i++)
            {
                Entity e = world[i];
                Vec p = e.pos;
                e.pos = new Vec(p.x + e.vel.x, p.y + e.vel.y, p.z + e.vel.z);
                acc += (int)e.pos.x & 1023; // maske: buyuk dunyada checksum taskini onler (signed tasma C'de UB)
            }
            // 2) frame-ici baglanti zinciri (500 Event + 500 Vec, tamami cop)
            Event ev = null;
            for (int i = 0; i < 500; i++)
            {
                Event ne = new Event();
                ne.kind = i & 7;
                ne.at = new Vec(i, f, 0);
                ne.prev = ev;
                ev = ne;
            }
            while (ev != null) { acc += ev.kind; ev = ev.prev; }
            // 3) string cop: birlestirme dongusu + karisik concat
            string s = "";
            for (int i = 0; i < 32; i++) { s += i; }
            acc += s.Length;
            string t = "frame:" + f + " hp:" + world[f % world.Length].hp;
            acc += t.Length;
            // 4) churn: her frame 50 entity yeniden dogar (tenured olum + genc tahsis)
            for (int i = 0; i < 50; i++)
            {
                int k = Rnd(world.Length);
                Entity e2 = new Entity();
                e2.pos = new Vec(k, k, k);
                e2.vel = new Vec(1, 1, 1);
                e2.name = "re" + k;
                e2.hp = k;
                world[k] = e2;
            }
            // 5) gecici dizi
            int[] tmp = new int[256];
            for (int i = 0; i < 256; i++) { tmp[i] = i * f; }
            acc += tmp[255];
            return acc;
        }
    }
}
