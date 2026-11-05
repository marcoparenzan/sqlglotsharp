using Xunit.Abstractions;
using Xunit.Sdk;

namespace SqlGlotSharp.CorpusTests;

[XunitTestCaseDiscoverer("SqlGlotSharp.CorpusTests.CorpusTheoryDiscoverer", "SqlGlotSharp.CorpusTests")]
public sealed class CorpusTheoryAttribute : TheoryAttribute;

public sealed class CorpusTheoryDiscoverer(IMessageSink sink) : TheoryDiscoverer(sink)
{
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod,
        IAttributeInfo theoryAttribute, object[] dataRow)
    {
        var test = new XunitTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow);
        test.Traits["Source"] = ((string)dataRow[1]).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        return [test];
    }
}
