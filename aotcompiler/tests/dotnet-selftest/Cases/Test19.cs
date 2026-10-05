namespace Demo19
{
    struct Vec3
    {
        public float x; public float y; public float z;
        public Vec3(float ax, float ay, float az) { x = ax; y = ay; z = az; }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public float Dot(Vec3 o) { return x * o.x + y * o.y + z * o.z; }
        public void Scale(float f) { x *= f; y *= f; z *= f; }
        public float SumX { get { return x; } set { x = value; } }
    }
    class Body
    {
        public Vec3 pos;
        int id;
        public Body(int i) { id = i; }
    }
    class App19
    {
        static int ModifyCopy(Vec3 p) { p.x = 100; return (int)p.x; }
        public static int Run()
        {
            Vec3 v = new Vec3(1, 2, 3);
            v.Scale(2);
            int acc = (int)(v.x + v.y + v.z);
            Vec3 w = v + new Vec3(1, 1, 1);
            acc += (int)w.Dot(new Vec3(1, 1, 1));
            w.SumX = 10;
            acc += (int)w.SumX;
            Vec3[] arr = new Vec3[3];
            arr[1] = w;
            arr[1].x = 20;
            arr[1].Scale(2);
            acc += (int)(arr[1].x + arr[1].y + arr[1].z);
            if ((int)w.x == 10) { acc += 5; }
            Body b = new Body(7);
            b.pos = new Vec3(1, 2, 3);
            b.pos.y = 9;
            b.pos.Scale(3);
            acc += (int)(b.pos.x + b.pos.y + b.pos.z);
            acc += ModifyCopy(v);
            acc += (int)v.x;
            return acc;
        }
    }
}
