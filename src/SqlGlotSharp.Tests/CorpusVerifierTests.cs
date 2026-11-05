using System.Text.Json;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class CorpusVerifierTests
{
    [Fact]
    public void L0_normalizes_unicode_positions_in_utf16()
    {
        using var payload = JsonDocument.Parse("""
        {"sql":"'😀'","read":"","expected":{"result":[{"token_type":"STRING","text":"😀","line":1,"col":4,"start":0,"end":3,"comments":[]}]}}
        """);
        CorpusVerifier.Verify(new("test", "L0", "base", [], payload.RootElement.Clone()));
    }

    [Fact]
    public void Concrete_jsonpath_tokenizer_identity_is_replayed()
    {
        using var payload = JsonDocument.Parse("""
        {"sql":"$","read":"postgres","tokenizer":"sqlglot.jsonpath::JSONPathTokenizer","expected":{"result":[{"token_type":"DOLLAR","text":"$","line":1,"col":1,"start":0,"end":0,"comments":[]}]}}
        """);
        CorpusVerifier.Verify(new("test", "L0", "postgres", [], payload.RootElement.Clone()));
    }

    [Fact]
    public void L0_mismatch_is_a_failure_not_a_pass()
    {
        using var payload = JsonDocument.Parse("""{"sql":"SELECT 1","read":"","expected":{"result":[]}}""");
        Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(new("test", "L0", "base", [], payload.RootElement.Clone())));
        Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(new("test", "L1", "base", [], payload.RootElement.Clone())));
    }
}
