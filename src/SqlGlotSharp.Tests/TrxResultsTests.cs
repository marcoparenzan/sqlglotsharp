using System.Text.Json;
using System.Xml.Linq;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class TrxResultsTests
{
    [Fact]
    public void Imports_real_outcomes_and_rejects_incomplete_duplicate_or_skipped_results()
    {
        using var json = JsonDocument.Parse("{}");
        var first = new CorpusCase(new string('a', 64), "L0", "base", [], json.RootElement.Clone());
        var second = new CorpusCase(new string('b', 64), "L1", "base", [], json.RootElement.Clone());
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement Result(CorpusCase item, string outcome, string message = "") => new(ns + "UnitTestResult",
            new XAttribute("testName", $"test(id: \"{item.Id}\")"), new XAttribute("outcome", outcome),
            new XElement(ns + "Output", new XElement(ns + "ErrorInfo", new XElement(ns + "Message", message))));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trx");
        try
        {
            void Save(params XElement[] results) => new XDocument(new XElement(ns + "TestRun", results)).Save(path);
            Save(Result(first, "Passed"), Result(second, "Failed", $"Not ported: corpus:L1:{second.Id}"));
            Assert.Equal(new[] { CaseStatus.Passed, CaseStatus.NotPorted }, TrxResults.Read(path, [first, second]).Select(r => r.Status));
            Save(Result(first, "Failed", "SQL mismatch"));
            Assert.Equal(CaseStatus.Failed, Assert.Single(TrxResults.Read(path, [first])).Status);
            Assert.Throws<InvalidDataException>(() => TrxResults.Read(path, [first, second]));
            Save(Result(first, "Passed"), Result(first, "Passed"));
            Assert.Throws<InvalidDataException>(() => TrxResults.Read(path, [first]));
            Save(Result(first, "NotExecuted"));
            Assert.Throws<InvalidDataException>(() => TrxResults.Read(path, [first]));
            Save(Result(second, "Passed"));
            Assert.Throws<InvalidDataException>(() => TrxResults.Read(path, [first]));
        }
        finally { File.Delete(path); }
    }
}
