using System.Text.Json;

namespace SqlGlotSharp.Corpus;

public static class CorpusVerifier
{
    public static void Verify(CorpusCase item)
    {
        if (item.Level == "L1") { L1Replay.Verify(item); return; }
        if (item.Level == "L2") { L2Replay.Verify(item); return; }
        if (item.Level == "L3") { L3Replay.Verify(item); return; }
        if (item.Level != "L0") throw new NotPortedException($"corpus:{item.Level}:{item.Id}");
        var sql = item.Payload.GetProperty("sql").GetString()!;
        var read = item.Payload.GetProperty("read").GetString()!;
        JsonElement actual;
        try
        {
            var tokenizer = item.Payload.TryGetProperty("tokenizer", out var identity)
                ? CreateTokenizer(identity.GetString()!, read) : tokens.Tokenizer.ForDialect(read);
            var offsets = CodePointOffsets(sql);
            var values = tokenizer.tokenize(sql).Select(token =>
            {
                var start = Offset(offsets, token.start);
                var endExclusive = Offset(offsets, token.end + 1);
                var lineStart = endExclusive == 0 ? 0 : sql.LastIndexOf('\n', endExclusive - 1, endExclusive) + 1;
                return new { token_type = token.token_type.ToString(), token.text, token.line,
                    col = endExclusive - lineStart, start, end = endExclusive - 1, token.comments };
            }).ToArray();
            actual = JsonSerializer.SerializeToElement(new { result = values });
        }
        catch (NotPortedException) { throw; }
        catch (Exception error)
        {
            actual = JsonSerializer.SerializeToElement(new { error = new { @class = error.GetType().Name,
                module = error is errors.SqlglotError ? "sqlglot.errors" : "builtins", message = error.Message } });
        }
        var expected = item.Payload.GetProperty("expected");
        if (!JsonElement.DeepEquals(expected, actual))
            throw new InvalidOperationException($"Corpus mismatch {item.Id}\nExpected: {expected.GetRawText()}\nActual: {actual.GetRawText()}");
    }

    private static tokens.Tokenizer CreateTokenizer(string identity, string read) => identity switch
    {
        "sqlglot.jsonpath::JSONPathTokenizer" or "sqlglot.dialects.dialect::JSONPathTokenizer" => new jsonpath.JSONPathTokenizer(read),
        "sqlglot.dialects.dialect::Tokenizer" => tokens.Tokenizer.ForDialect(read),
        "sqlglot.tokens::Tokenizer" => new tokens.Tokenizer(read),
        "sqlglot.dialects.postgres::Postgres.Tokenizer" => new dialects.postgres.Postgres.Tokenizer(read),
        "sqlglot.dialects.tsql::TSQL.Tokenizer" => new dialects.tsql.TSQL.Tokenizer(read),
        "sqlglot.dialects.fabric::Fabric.Tokenizer" => new dialects.fabric.Fabric.Tokenizer(read),
        _ => throw new NotPortedException($"corpus:tokenizer:{identity}")
    };

    private static int[] CodePointOffsets(string text)
    {
        List<int> offsets = [0];
        for (var i = 0; i < text.Length;)
        {
            i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            offsets.Add(i);
        }
        return offsets.ToArray();
    }
    private static int Offset(int[] offsets, int index) => offsets[Math.Clamp(index < 0 ? offsets.Length - 1 + index : index, 0, offsets.Length - 1)];
}
