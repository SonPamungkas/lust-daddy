using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LustDaddy
{
    public enum PassKind { First, Before, For, After, Last }

    public static class PatchOrdering
    {
        public static (PassKind kind, string author) ParsePass(string pass, string ownIdentity)
        {
            if (string.IsNullOrWhiteSpace(pass))
                return (PassKind.For, ownIdentity);

            string p = pass.Trim();
            if (p.Equals("FIRST", StringComparison.OrdinalIgnoreCase)) return (PassKind.First, null);
            if (p.Equals("LAST", StringComparison.OrdinalIgnoreCase)) return (PassKind.Last, null);

            var m = Regex.Match(p, @"^(BEFORE|FOR|AFTER)\s*\[\s*([^\]]+)\s*\]$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string author = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value.ToUpperInvariant())
                {
                    case "BEFORE": return (PassKind.Before, author);
                    case "AFTER": return (PassKind.After, author);
                    default: return (PassKind.For, author);
                }
            }

            return (PassKind.For, ownIdentity);
        }

        public static List<(string file, UnitModConfig cfg)> Order(List<(string file, UnitModConfig cfg)> configs)
        {
            var parsed = configs.Select(c =>
            {
                string identity = !string.IsNullOrEmpty(c.cfg.patchAuthor)
                    ? c.cfg.patchAuthor
                    : System.IO.Path.GetFileNameWithoutExtension(c.file);
                var (kind, author) = ParsePass(c.cfg.pass, identity);
                return (c.file, c.cfg, identity, kind, author);
            }).ToList();

            var authors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parsed)
            {
                authors.Add(p.identity);
                if (p.kind != PassKind.First && p.kind != PassKind.Last && !string.IsNullOrEmpty(p.author))
                    authors.Add(p.author);
            }

            var result = new List<(string file, UnitModConfig cfg)>();

            result.AddRange(parsed.Where(p => p.kind == PassKind.First)
                .OrderBy(p => p.file, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p.file, p.cfg)));

            foreach (var author in authors)
            {
                result.AddRange(parsed.Where(p => p.kind == PassKind.Before && string.Equals(p.author, author, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.file, StringComparer.OrdinalIgnoreCase).Select(p => (p.file, p.cfg)));
                result.AddRange(parsed.Where(p => p.kind == PassKind.For && string.Equals(p.author, author, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.file, StringComparer.OrdinalIgnoreCase).Select(p => (p.file, p.cfg)));
                result.AddRange(parsed.Where(p => p.kind == PassKind.After && string.Equals(p.author, author, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.file, StringComparer.OrdinalIgnoreCase).Select(p => (p.file, p.cfg)));
            }

            result.AddRange(parsed.Where(p => p.kind == PassKind.Last)
                .OrderBy(p => p.file, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p.file, p.cfg)));

            return result;
        }
    }
}
