using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SqlGlotSharp.Corpus;

public static class TrxResults
{
    public static IReadOnlyList<CaseResult> Read(string path, IEnumerable<CorpusCase> corpus)
    {
        var cases = corpus.ToArray();
        var byId = cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<CaseResult> results = [];
        foreach (var node in document.Descendants(ns + "UnitTestResult"))
        {
            var name = (string?)node.Attribute("testName") ?? "";
            var message = node.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "Message")?.Value ?? "";
            var anchor = Regex.Match(message, @"Not ported: corpus:L[0-3]:([a-f0-9]{64})");
            var prefix = anchor.Success ? anchor.Groups[1].Value : Regex.Match(name, "id: \"([a-f0-9]+)").Groups[1].Value;
            if (prefix.Length < 16) throw new InvalidDataException($"Cannot identify corpus case: {name}");
            var matches = byId.TryGetValue(prefix, out var exact) ? new[] { exact } :
                cases.Where(c => c.Id.StartsWith(prefix, StringComparison.Ordinal)).Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"Unknown or ambiguous corpus ID: {prefix}");
            var item = matches[0];
            if (!seen.Add(item.Id)) throw new InvalidDataException($"Duplicate result: {item.Id}");
            var outcome = (string?)node.Attribute("outcome");
            var status = outcome switch
            {
                "Passed" => CaseStatus.Passed,
                "Failed" when anchor.Success => CaseStatus.NotPorted,
                "Failed" => CaseStatus.Failed,
                _ => throw new InvalidDataException($"Unsupported outcome '{outcome}' for {item.Id}; skipped cases cannot count as passes")
            };
            results.Add(new(item, status, message));
        }
        if (seen.Count != cases.Length) throw new InvalidDataException($"Incomplete TRX: {seen.Count}/{cases.Length} cases");
        return results;
    }
}
