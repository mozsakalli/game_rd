namespace System.Text.RegularExpressions
{
    delegate string MatchEvaluator(Match match);

    class Group
    {
        string value;
        public Group(string value) { this.value = value; }
        public string Value { get { return value; } }
    }

    class GroupCollection
    {
        Group[] groups;
        public GroupCollection(Group[] groups) { this.groups = groups; }
        public Group this[int index] { get { return groups[index]; } }
    }

    class Match
    {
        string input;
        string prefix;
        string suffix;
        int index;
        int length;
        bool success;
        GroupCollection groups;

        public Match(string input, string prefix, string suffix, int start)
        {
            this.input = input;
            this.prefix = prefix;
            this.suffix = suffix;
            index = input.IndexOf(prefix, start);
            if (index < 0)
            {
                success = false;
                groups = new GroupCollection(new Group[0]);
                return;
            }
            int captureStart = index + prefix.Length;
            int end = input.IndexOf(suffix, captureStart);
            if (end < 0 || end == captureStart)
            {
                success = false;
                groups = new GroupCollection(new Group[0]);
                return;
            }
            success = true;
            length = end + suffix.Length - index;
            groups = new GroupCollection(new Group[] {
                new Group(input.Substring(index, length)),
                new Group(input.Substring(captureStart, end - captureStart))
            });
        }

        public bool Success { get { return success; } }
        public int Index { get { return index; } }
        public int Length { get { return length; } }
        public GroupCollection Groups { get { return groups; } }
        public Match NextMatch() { return success ? new Match(input, prefix, suffix, index + length) : this; }
    }

    static class Regex
    {
        static string Prefix(string pattern)
        {
            if (pattern == "\\{locale:([^\\}]+)\\}")
                return "{locale:";
            if (pattern == "Texture_Pixel\\(([^\\)]+)\\)")
                return "Texture_Pixel(";
            throw new NotImplementedException();
        }

        static string Suffix(string pattern)
        {
            if (pattern == "\\{locale:([^\\}]+)\\}") return "}";
            if (pattern == "Texture_Pixel\\(([^\\)]+)\\)") return ")";
            throw new NotImplementedException();
        }

        public static Match Match(string input, string pattern) { return new Match(input, Prefix(pattern), Suffix(pattern), 0); }

        public static string Replace(string input, string pattern, MatchEvaluator evaluator)
        {
            string result = "";
            int start = 0;
            Match match = Match(input, pattern);
            while (match.Success)
            {
                if (match.Index > start) result += input.Substring(start, match.Index - start);
                result += evaluator(match);
                start = match.Index + match.Length;
                match = match.NextMatch();
            }
            if (start < input.Length) result += input.Substring(start);
            return result;
        }
    }
}