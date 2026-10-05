namespace System
{
    struct DateTime
    {
        public static extern DateTime Now { get; }
        public extern long Ticks { get; }
    }
}