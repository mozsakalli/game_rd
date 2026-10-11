namespace System
{
    public sealed class Random
    {
        public readonly static Random Shared = new Random();
        int _seed = Environment.TickCount;

        public int Next()
        {
            unchecked
            {
                _seed = _seed * 214013 + 2531011;
                return (_seed >> 16) & 0x7FFF;
            }
        }

        public double NextDouble() => Next() / (float)short.MaxValue;

        public int Next(int maxValue) => Next() % maxValue;
        public int Next(int minValue, int maxValue) => minValue + Next(maxValue - minValue);
    }
}