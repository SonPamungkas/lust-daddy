using System;
using System.Linq;
using BepInEx.Bootstrap;

namespace LustDaddy
{
    public static class NeedsChecker
    {
        public static bool CheckExpression(string needsExpression)
        {
            if (string.IsNullOrEmpty(needsExpression)) return true;

            foreach (string andDependencies in needsExpression.Split(',', '&'))
            {
                bool orMatch = false;
                foreach (string orDependency in andDependencies.Split('|'))
                {
                    if (orDependency.Length == 0) continue;

                    bool not = orDependency[0] == '!';
                    string toFind = (not ? orDependency.Substring(1) : orDependency).Trim();
                    if (toFind.Length == 0) continue;

                    bool found = IsModLoaded(toFind);
                    if (not == !found) { orMatch = true; break; }
                }
                if (!orMatch) return false;
            }
            return true;
        }

        private static bool IsModLoaded(string mod)
        {
            try
            {
                return Chainloader.PluginInfos.Values.Any(p =>
                    string.Equals(p.Metadata.GUID, mod, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Metadata.Name, mod, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
