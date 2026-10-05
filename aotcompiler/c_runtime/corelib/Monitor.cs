namespace System.Threading
{
    static class Monitor
    {
        public static extern void Enter(object target);
        public static extern void Exit(object target);
    }
}