using System.Text.RegularExpressions;

namespace LustDaddy
{
    public static class Wildcard
    {
        public static bool HasWildcard(string s) => !string.IsNullOrEmpty(s) && (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0);

        public static bool IsMatch(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (text == null) return false;
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
        }
    }
}
