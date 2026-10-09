using System;

internal static class Program
{
    private const int CaseCount = 59;

    // CIL frontend dostu dispatch: delegate dizisi/generic yok, duz switch.
    private static int RunCase(int number)
    {
        switch (number)
        {
            case 1: return Demo.App.Run();
            case 2: return Demo2.App2.Run();
            case 3: return Demo3.App3.Run();
            case 4: return Demo4.App4.Run();
            case 5: return Demo5.App5.Run();
            case 6: return Demo6.App6.Run();
            case 7: return Demo7.App7.Run();
            case 8: return Demo8.App8.Run();
            case 9: return Demo9.App9.Run();
            case 10: return Demo10.App10.Run();
            case 11: return Demo11.App11.Run();
            case 12: return Demo12.App12.Run();
            case 13: return Demo13.App13.Run();
            case 14: return Demo14.App14.Run();
            case 15: return Demo15.App15.Run();
            case 16: return Demo16.App16.Run();
            case 17: return Demo17.App17.Run();
            case 18: return Demo18.App18.Run();
            case 19: return Demo19.App19.Run();
            case 20: return Demo20.App20.Run();
            case 21: return Demo21.App21.Run();
            case 22: return Demo22.App22.Run();
            case 23: return Demo23.App23.Run();
            case 24: return Demo24.App24.Run();
            case 25: return Demo25.App25.Run();
            case 26: return Demo26.App26.Run();
            case 27: return Demo27.App27.Run();
            case 28: return Demo28.App28.Run();
            case 29: return Demo29.App29.Run();
            case 30: return Demo30.App30.Run();
            case 31: return Demo31.App31.Run();
            case 32: return Demo32.App32.Run();
            case 33: return Demo33.App33.Run();
            case 34: return Demo34.App34.Run();
            case 35: return Demo35.App35.Run();
            case 36: return Demo36.App36.Run();
            case 37: return Demo37.App37.Run();
            case 38: return Demo38.App38.Run();
            case 39: return Demo39.App39.Run();
            case 40: return Demo40.App40.Run();
            case 41: return Demo41.App41.Run();
            case 42: return Demo42.App42.Run();
            case 43: return Demo43.App43.Run();
            case 44: return Demo44.App44.Run();
            case 45: return Demo45.App45.Run();
            case 46: return Demo46.App46.Run();
            case 47: return Demo47.App47.Run();
            case 48: return Demo48.App48.Run();
            case 49: return Demo49.App49.Run();
            case 51: return Demo51.App51.Run();
            case 52: return Demo52.App52.Run();
            case 53: return Demo53.App53.Run();
            case 54: return Demo54.App54.Run();
            case 55: return Demo55.App55.Run();
            case 56: return Demo56.App56.Run();
            case 57: return Demo57.App57.Run();
            case 58: return Demo58.App58.Run();
                case 59: return Demo59.App59.Run();
            default: return 0;
        }
    }

    // Sadece "N<TAB>sonuc" satirlari; File I/O yok, string interpolation yok (CIL dostu).
    private static int Main()
    {
        for (int number = 1; number <= CaseCount; number++)
        {
            try
            {
                int actual = RunCase(number);
                Console.Write(number);
                Console.Write("\t");
                Console.WriteLine(actual);
            }
            catch (Exception cause)
            {
                Console.Write(number);
                Console.Write("\tEXCEPTION\t");
                Console.Write(cause.GetType().Name);
                Console.Write(": ");
                Console.WriteLine(cause.Message);
                Console.Error.WriteLine(cause.StackTrace); // tani: diff'e girmez (N<TAB> onekli degil)
            }
        }
        return 0;
    }
}