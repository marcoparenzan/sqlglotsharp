using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class py
{
    public static class re
    {
        private static readonly ConcurrentDictionary<(string, long), Pattern> Cache = new();
        public static Pattern compile(string pattern, long flags = 0) => Cache.GetOrAdd((pattern, flags), key => new Pattern(key.Item1, key.Item2));
        public static Match? search(string pattern, string text, long flags = 0) => compile(pattern, flags).search(text);
        public static Match? match(string pattern, string text, long flags = 0) => compile(pattern, flags).match(text);
        public static string sub(string pattern, string replacement, string text, int count = 0, long flags = 0) =>
            compile(pattern, flags).sub(replacement, text, count);

        /// <summary>Incremental adapter for generated regex data; full Python regex operations remain phase 2.</summary>
        public sealed class Pattern
        {
            public string pattern { get; }
            public long flags { get; }
            private readonly Regex regex;
            internal Pattern(string pattern, long flags)
            {
                this.pattern = pattern;
                this.flags = flags;
                if ((flags & ~(2L | 8L | 16L | 32L | 64L | 256L)) != 0)
                    throw new NotSupportedException("Python regex flags not implemented.");
                var options = RegexOptions.CultureInvariant;
                if ((flags & 2) != 0) options |= RegexOptions.IgnoreCase;
                if ((flags & 8) != 0) options |= RegexOptions.Multiline;
                if ((flags & 16) != 0) options |= RegexOptions.Singleline;
                if ((flags & 64) != 0) options |= RegexOptions.IgnorePatternWhitespace;
                // Python's Unicode \w excludes combining marks, unlike .NET's \w.
                var word = (flags & 256) != 0 ? "[a-zA-Z0-9_]" : "[\\p{L}\\p{N}_]";
                var translated = pattern.Replace(@"[\w]", word, StringComparison.Ordinal)
                    .Replace(@"\w", word, StringComparison.Ordinal)
                    .Replace("(?P<", "(?<", StringComparison.Ordinal)
                    .Replace(@"\Z", @"\z", StringComparison.Ordinal);
                translated = Regex.Replace(translated, @"\(\?P=([A-Za-z_][A-Za-z_0-9]*)\)", m => @"\k<" + m.Groups[1].Value + ">");
                translated = NumberCaptures(translated, (options & RegexOptions.IgnorePatternWhitespace) != 0);
                regex = new Regex(translated, options, TimeSpan.FromSeconds(2));
            }
            // .NET numbers unnamed captures before named captures. Naming every capture
            // makes .NET assign all numbers in textual order, matching Python.
            private static readonly Regex InlineFlagRegex = new(@"\G\(\?([imsx]*)(?:-([imsx]+))?([:)])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

            private static string NumberCaptures(string value, bool verbose)
            {
                var output = new System.Text.StringBuilder();
                var inClass = false;
                var number = 0;
                var scopes = new Stack<bool>();
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    if (c == '\\')
                    {
                        output.Append(c);
                        if (i + 1 < value.Length) output.Append(value[++i]);
                        continue;
                    }
                    if (!inClass && verbose && c == '#')
                    {
                        while (i < value.Length && value[i] != '\n') output.Append(value[i++]);
                        if (i < value.Length) output.Append('\n');
                        continue;
                    }
                    if (c == '[') inClass = true;
                    if (c == ']') inClass = false;
                    if (!inClass && c == '(')
                    {
                        if (i + 2 < value.Length && value[i + 1] == '?' && value[i + 2] == '#')
                        {
                            var end = value.IndexOf(')', i + 3);
                            if (end < 0) throw new ArgumentException("unterminated regex comment");
                            output.Append(value.AsSpan(i, end - i + 1)); i = end; continue;
                        }
                        var inlineFlags = InlineFlagRegex.Match(value, i);
                        if (inlineFlags.Success && inlineFlags.Index == i &&
                            (inlineFlags.Groups[1].Length != 0 || inlineFlags.Groups[2].Length != 0))
                        {
                            var scoped = inlineFlags.Groups[3].Value == ":";
                            if (scoped) scopes.Push(verbose);
                            if (inlineFlags.Groups[1].Value.Contains('x')) verbose = true;
                            if (inlineFlags.Groups[2].Value.Contains('x')) verbose = false;
                            output.Append(inlineFlags.Value);
                            i += inlineFlags.Length - 1;
                            continue;
                        }
                        scopes.Push(verbose);
                        if (i + 1 >= value.Length || value[i + 1] != '?')
                        {
                            string name;
                            do { name = "__sqlglot_capture_" + ++number; } while (value.Contains("(?<" + name + ">", StringComparison.Ordinal));
                            output.Append("(?<").Append(name).Append('>');
                            continue;
                        }
                    }
                    if (!inClass && c == ')' && scopes.TryPop(out var previous)) verbose = previous;
                    output.Append(c);
                }
                return output.ToString();
            }

            public string sub(string replacement, string text, int count = 0)
            {
                // Parse before matching: invalid references must fail even when nothing matches.
                var parts = ParseReplacement(replacement);
                return sub(m => string.Concat(parts.Select(p => p.Group is null ? p.Text : m.Groups[p.Group].Value)), text, count);
            }

            public string sub(Func<Match, string> replacement, string text, int count = 0) =>
                count < 0 ? text : regex.Replace(text, m => replacement(m), count == 0 ? int.MaxValue : count);

            private List<(string? Text, string? Group)> ParseReplacement(string value)
            {
                var parts = new List<(string? Text, string? Group)>();
                var groups = regex.GetGroupNames().ToHashSet(StringComparer.Ordinal);
                void Group(string name)
                {
                    if (name.All(char.IsAsciiDigit) && int.TryParse(name, out var index))
                    {
                        if (!regex.GetGroupNumbers().Contains(index)) throw new ArgumentException($"invalid group reference: {name}");
                        name = regex.GroupNameFromNumber(index);
                    }
                    if (!groups.Contains(name)) throw new ArgumentException($"invalid group reference: {name}");
                    parts.Add((null, name));
                }
                for (var i = 0; i < value.Length; i++)
                {
                    if (value[i] != '\\') { parts.Add((value[i].ToString(), null)); continue; }
                    if (++i == value.Length) throw new ArgumentException("bad escape at end of replacement");
                    var c = value[i];
                    if (c == 'g' && i + 1 < value.Length && value[i + 1] == '<')
                    {
                        var end = value.IndexOf('>', i + 2);
                        if (end < 0) throw new ArgumentException("unterminated group reference");
                        Group(value[(i + 2)..end]); i = end;
                    }
                    else if (c == '0' || c is >= '1' and <= '7' && i + 2 < value.Length &&
                             value[i + 1] is >= '0' and <= '7' && value[i + 2] is >= '0' and <= '7')
                    {
                        var octal = c.ToString();
                        while (octal.Length < 3 && i + 1 < value.Length && value[i + 1] is >= '0' and <= '7')
                            octal += value[++i];
                        var code = Convert.ToInt32(octal, 8);
                        if (code > 255) throw new ArgumentException("octal escape value outside of range 0-0o377");
                        parts.Add((((char)code).ToString(), null));
                    }
                    else if (c is >= '1' and <= '9')
                    {
                        var name = c.ToString();
                        if (i + 1 < value.Length && char.IsAsciiDigit(value[i + 1])) name += value[++i];
                        Group(name);
                    }
                    else
                    {
                        var literal = c switch
                        {
                            '\\' => "\\", 'n' => "\n", 'r' => "\r", 't' => "\t", 'a' => "\a",
                            'b' => "\b", 'f' => "\f", 'v' => "\v",
                            _ when char.IsAsciiLetter(c) => throw new ArgumentException($"bad escape: {c}"),
                            _ => "\\" + c
                        };
                        parts.Add((literal, null));
                    }
                }
                return parts;
            }

            public Match? search(string text)
            {
                var result = regex.Match(text);
                return result.Success ? result : null;
            }
            public Match? match(string text)
            {
                var result = search(text);
                return result?.Index == 0 ? result : null;
            }
        }
    }
}
