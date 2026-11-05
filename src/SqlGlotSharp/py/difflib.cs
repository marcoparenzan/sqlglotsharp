using System.Text;

namespace SqlGlotSharp;

public static partial class py
{
    public static class difflib
    {
        public static string? closest(string word, IEnumerable<string> possibilities, double cutoff = 0.6)
        {
            string? best = null;
            var bestRatio = cutoff;
            foreach (var candidate in possibilities)
            {
                var ratio = sequence_ratio(candidate, word);
                if (ratio >= bestRatio && (best is null || ratio > bestRatio || CompareCodePoints(candidate, best) > 0))
                { best = candidate; bestRatio = ratio; }
            }
            return best;
        }

        // SequenceMatcher matching-block algorithm, with no junk predicate and default autojunk.
        public static double sequence_ratio(string first, string second)
        {
            var a = first.EnumerateRunes().Select(r => r.Value).ToArray();
            var b = second.EnumerateRunes().Select(r => r.Value).ToArray();
            if (a.Length + b.Length == 0) return 1;
            var indexes = new Dictionary<int, List<int>>();
            for (var j = 0; j < b.Length; j++)
            {
                if (!indexes.TryGetValue(b[j], out var positions)) indexes[b[j]] = positions = [];
                positions.Add(j);
            }
            if (b.Length >= 200)
                foreach (var key in indexes.Where(p => p.Value.Count > b.Length / 100 + 1).Select(p => p.Key).ToArray()) indexes.Remove(key);

            (int I, int J, int Size) Longest(int alo, int ahi, int blo, int bhi)
            {
                var besti = alo; var bestj = blo; var bestsize = 0;
                Dictionary<int, int> lengths = [];
                for (var i = alo; i < ahi; i++)
                {
                    Dictionary<int, int> next = [];
                    if (indexes.TryGetValue(a[i], out var positions))
                        foreach (var j in positions)
                        {
                            if (j < blo) continue;
                            if (j >= bhi) break;
                            var size = lengths.GetValueOrDefault(j - 1) + 1;
                            next[j] = size;
                            if (size > bestsize) { besti = i - size + 1; bestj = j - size + 1; bestsize = size; }
                        }
                    lengths = next;
                }
                // Popular characters are not junk: extend through them around the anchor.
                while (besti > alo && bestj > blo && a[besti - 1] == b[bestj - 1]) { besti--; bestj--; bestsize++; }
                while (besti + bestsize < ahi && bestj + bestsize < bhi && a[besti + bestsize] == b[bestj + bestsize]) bestsize++;
                return (besti, bestj, bestsize);
            }

            var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
            queue.Push((0, a.Length, 0, b.Length));
            var total = 0;
            while (queue.TryPop(out var area))
            {
                var (i, j, size) = Longest(area.Alo, area.Ahi, area.Blo, area.Bhi);
                if (size == 0) continue;
                total += size;
                if (area.Alo < i && area.Blo < j) queue.Push((area.Alo, i, area.Blo, j));
                if (i + size < area.Ahi && j + size < area.Bhi) queue.Push((i + size, area.Ahi, j + size, area.Bhi));
            }
            return 2.0 * total / (a.Length + b.Length);
        }

        private static int CompareCodePoints(string first, string second)
        {
            using var a = first.EnumerateRunes().Select(r => r.Value).GetEnumerator();
            using var b = second.EnumerateRunes().Select(r => r.Value).GetEnumerator();
            while (a.MoveNext())
            {
                if (!b.MoveNext()) return 1;
                var comparison = a.Current.CompareTo(b.Current);
                if (comparison != 0) return comparison;
            }
            return b.MoveNext() ? -1 : 0;
        }
    }
}
