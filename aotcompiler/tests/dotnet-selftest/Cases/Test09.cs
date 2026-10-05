namespace Demo9
{
    class Shape9
    {
        public virtual int Sides { get { return 0; } }
    }
    class Square9 : Shape9
    {
        public override int Sides { get { return 4; } }
        public int Size { get; set; }
        public int Area { get { return Size * Size; } }
    }
    class Cfg9
    {
        public static int Level { get; set; } = 7;
        public static int Doubled { get { return Level * 2; } }
    }
    class App9
    {
        int hp;
        int Hp { get { return hp; } set { hp = value + 1; } }
        public static int Run()
        {
            App9 a = new App9();
            a.Hp = 10;
            int acc = a.Hp;
            Square9 s = new Square9();
            s.Size = 5;
            acc = acc + s.Area;
            Shape9 sh = s;
            acc = acc + sh.Sides * 100;
            acc = acc + Cfg9.Level;
            Cfg9.Level = 20;
            acc = acc + Cfg9.Doubled;
            return acc;
        }
    }
}
