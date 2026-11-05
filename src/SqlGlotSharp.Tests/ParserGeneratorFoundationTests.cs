using Xunit;
using SqlGlotSharp;
using ErrorLevel = SqlGlotSharp.errors.ErrorLevel;

namespace SqlGlotSharp.Tests;

public class ParserGeneratorFoundationTests
{
    [Fact]
    public void ImmediateAndDeferredParserErrors()
    {
        var immediate = new parser.Parser();
        Assert.Throws<errors.ParseError>(() => immediate.raise_error("Bad input"));
        var deferred = new parser.Parser(error_level: ErrorLevel.RAISE);
        deferred.raise_error("First");
        deferred.raise_error("Second");
        Assert.Equal(2, deferred.errors.Count);
        var error = Assert.Throws<errors.ParseError>(() => deferred.check_errors());
        Assert.Contains("First", error.Message);
        Assert.Contains("Second", error.Message);
        deferred.reset();
        Assert.Empty(deferred.errors);
    }

    [Fact]
    public void NodeLimitAppliesEvenWhenValidationIgnored()
    {
        var parser = new parser.Parser(error_level: ErrorLevel.IGNORE, max_nodes: 1);
        parser.validate_expression(new exp.Identifier());
        parser.validate_expression(new exp.Identifier());
        Assert.Single(parser.errors);
        Assert.Contains("Maximum number", parser.errors[0].Message);
        parser.check_errors();
        Assert.Equal(2, parser._node_count);
    }

    [Fact]
    public void ParserAddsCommentsAndValidatesRequiredArguments()
    {
        var parser = new parser.Parser();
        parser._prev_comments.Add("hello");
        var identifier = parser.expression(new exp.Identifier(@this: "x"));
        Assert.Contains("hello", identifier.comments!);
        Assert.Empty(parser._prev_comments);
        Assert.Throws<errors.ParseError>(() => parser.validate_expression(new exp.Identifier()));
    }

    [Fact]
    public void GeneratorFormattingAndNormalization()
    {
        var compact = new generator.Generator();
        Assert.Equal(" SELECT", compact.seg("SELECT"));
        Assert.Equal("a\nb", compact.indent("a\nb", level: 1));
        var pretty = new generator.Generator(pretty: true);
        Assert.Equal("\nSELECT", pretty.seg("SELECT"));
        Assert.Equal("    a\n    b", pretty.indent("a\nb", level: 1));
        Assert.Equal("a\n  b", pretty.indent("a\nb", skip_first: true));
        Assert.Equal("SUM", pretty.normalize_func("sum"));
        Assert.Equal("sum", new generator.Generator(normalize_functions: "lower").normalize_func("SUM"));
        Assert.Equal("()", compact.wrap(""));
    }

    [Fact]
    public void UnsupportedDiagnosticsAndExplicitBoundaries()
    {
        var immediate = new generator.Generator(unsupported_level: ErrorLevel.IMMEDIATE);
        Assert.Throws<errors.UnsupportedError>(() => immediate.unsupported("unsupported"));
        var deferred = new generator.Generator(unsupported_level: ErrorLevel.RAISE);
        deferred.unsupported("unsupported");
        Assert.Single(deferred.unsupported_messages);
        Assert.Equal("x", deferred.sql(new exp.Identifier(@this: "x")));
        Assert.Equal("x", deferred.generate(new exp.Identifier(@this: "x")));
        Assert.Empty(deferred.unsupported_messages);
        Assert.Equal("x", deferred.sql("x"));
        Assert.Equal("", deferred.sql(null));
    }
}
