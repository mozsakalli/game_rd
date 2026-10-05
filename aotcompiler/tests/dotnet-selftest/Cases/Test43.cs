namespace Demo43 {
    class App43 {
        public static int Run() {
            string p = "beep";
            int result = 0;
            if ($"sound/{p}" == "sound/beep") { result += 1; }
            if ($"{{{p}}}" == "{beep}") { result += 2; }
            return result;
        }
    }
}
