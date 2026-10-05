using System;
namespace Demo45 {
    class App45 {
        static bool IsReadOnly => throw new NotImplementedException();
        public static int Run() {
            try { bool value = IsReadOnly; }
            catch (NotImplementedException) { return 1; }
            return 0;
        }
    }
}
