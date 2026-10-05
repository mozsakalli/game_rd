namespace Demo51
{
    // Kusakli GC write-barrier regresyonu (gcbench deseni, deterministik): eski (tenured) sahiplere
    // genc referans yazimi 3 bicimde — alan (stfld), ref-dizi elemani (stelem), struct elemani/alani
    // uzerinden ic isaretci (ldelema/ldflda + stobj/stfld) ve ref parametre. Her "frame" GC.Collect(0)
    // (= gc_minor) kosar; bariyer eksikse genc nesneler toplanir, geri okunan degerler bozulur.
    class Vec { public int x; public Vec(int v) { x = v; } }
    class Entity { public Vec pos; public Vec vel; public string name; }
    struct Slot { public Vec v; public int k; }
    class Holder { public Slot slot; public Slot[] slots; }
    class App51
    {
        static Entity[] world;
        static System.Collections.Generic.List<Entity> list;
        static System.Collections.Generic.Dictionary<int, Slot> map;
        static Holder holder;

        static void Fill(ref Slot s, int k) { s.v = new Vec(k); s.k = k; }

        public static int Run()
        {
            world = new Entity[64];
            list = new System.Collections.Generic.List<Entity>();
            map = new System.Collections.Generic.Dictionary<int, Slot>();
            holder = new Holder();
            holder.slots = new Slot[16];
            for (int i = 0; i < world.Length; i++)
            {
                Entity e = new Entity();
                e.pos = new Vec(i); e.vel = new Vec(1); e.name = "e" + i;
                world[i] = e;
            }
            // sahipler tenure olsun (GC_TENURE=3): 4 minor
            for (int g = 0; g < 4; g++) System.GC.Collect(0);

            int acc = 0;
            for (int f = 0; f < 40; f++)
            {
                // 1) stfld: eski entity -> genc Vec
                for (int i = 0; i < world.Length; i++)
                {
                    Entity e = world[i];
                    e.pos = new Vec(e.pos.x + e.vel.x);
                }
                // 2) stelem ref: eski dizi -> genc entity (churn)
                Entity n = new Entity(); n.pos = new Vec(f); n.vel = new Vec(2); n.name = "n" + f;
                world[f % world.Length] = n;
                list.Add(n); // eski List.items -> genc
                // 3) ldelema/stobj: eski struct dizisine genc ref tasiyan struct
                Slot s; s.v = new Vec(f * 3); s.k = f;
                holder.slots[f % 16] = s;
                // 3b) ldflda/stfld: eski nesnenin struct alanina ic yazim
                holder.slot.v = new Vec(f * 5);
                // 3c) ref parametre: callee sahibi bilmez
                Fill(ref holder.slots[(f + 1) % 16], f * 7);
                // 3d) Dictionary<int, struct>: values[] = struct (ldelema/stobj corelib'de)
                Slot ms; ms.v = new Vec(f * 11); ms.k = f;
                map[f % 8] = ms;

                System.GC.Collect(0); // minor: bariyersiz -> yukaridaki genc nesneler olur

                for (int i = 0; i < world.Length; i++) acc += world[i].pos.x & 1023;
                acc += list[list.Count - 1].pos.x;
                acc += holder.slots[f % 16].v.x + holder.slot.v.x + holder.slots[(f + 1) % 16].v.x;
                acc += map[f % 8].v.x;
                acc += world[f % world.Length].name.Length;
            }
            return acc;
        }
    }
}