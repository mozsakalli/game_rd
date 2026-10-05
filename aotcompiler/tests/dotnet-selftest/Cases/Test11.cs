#define FAST
namespace Demo11 {
    class App11 {
        public static int Run() {
            int acc = 0;
#if GAMOS
            acc = acc + 1;
#else
            bu satir gecerli kod degil ((( parse edilmemeli
#endif
#if FAST && !SLOW
            acc = acc + 10;
#elif SLOW
            acc = acc + 100;
#else
            acc = acc + 1000;
#endif
#if MISSING
            acc = acc + 10000;
#endif
            return acc;
        }
    }
}
