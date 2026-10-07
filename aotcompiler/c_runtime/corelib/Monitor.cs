namespace System.Threading
{
    public static class Monitor
    {
        public static extern void Enter(object target);
        public static extern void Exit(object target);
    }
}