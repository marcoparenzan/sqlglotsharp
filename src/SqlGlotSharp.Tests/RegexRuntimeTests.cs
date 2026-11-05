namespace SqlGlotSharp.Tests;

public class RegexRuntimeTests
{
    [Fact]
    public void Named_backreferences_match_python_syntax()
    {
        var pattern = py.re.compile(@"(?P<word>[a-z]+)-(?P=word)\Z");
        Assert.NotNull(pattern.match("abc-abc"));
        Assert.Null(pattern.match("abc-def"));
        Assert.Null(pattern.match("abc-abc\n"));
    }

    [Fact]
    public void Sub_supports_named_numeric_and_whole_match_references()
    {
        Assert.Equal("b:a b:c", py.re.sub(@"([a-z])([a-z])", @"\2:\1", "ab cb"));
        Assert.Equal("<ab> <cd>", py.re.sub(@"(?P<w>[a-z]+)", @"<\g<w>>", "ab cd"));
        Assert.Equal("$ab$", py.re.sub("ab", @"$\g<0>$", "ab"));
        Assert.Equal("\\\n", py.re.sub("x", @"\\\n", "x"));
    }

    [Fact]
    public void Sub_limits_counts_and_supports_callbacks_and_empty_matches()
    {
        var pattern = py.re.compile("x");
        Assert.Equal("axx", pattern.sub("a", "xxx", 1));
        Assert.Equal("aaa", pattern.sub("a", "xxx"));
        Assert.Equal("xxx", pattern.sub("a", "xxx", -1));
        Assert.Equal("X X", pattern.sub(m => m.Value.ToUpperInvariant(), "x x"));
        Assert.Equal("-a-b-", py.re.sub("", "-", "ab"));
    }

    [Fact]
    public void Mixed_capture_numbers_follow_python_textual_order()
    {
        var pattern = py.re.compile(@"(?P<first>a)(b)(?:x)(?P<last>c)\1\2\3");
        var match = pattern.match("abxcabc");
        Assert.NotNull(match);
        Assert.Equal("a", match.Groups[1].Value);
        Assert.Equal("b", match.Groups[2].Value);
        Assert.Equal("c", match.Groups[3].Value);
        Assert.Equal("a", match.Groups["first"].Value);
        Assert.Equal("c:b:a", pattern.sub(@"\3:\2:\1", "abxcabc"));
        Assert.Equal("b:a", py.re.sub(@"(a)(?P<n>b)", @"\2:\1", "ab"));
    }

    [Fact]
    public void Capture_scanner_preserves_literal_parentheses_and_comments()
    {
        Assert.NotNull(py.re.match(@"\((?P<n>a)[()](b)\)", "(a(b)"));
        Assert.Equal("b:a", py.re.sub("(?P<n>a) # ignored ( capture\n(b)", @"\2:\1", "ab", flags: 64));
        Assert.Equal("b:a", py.re.sub(@"(?P<n>a)(?#ignored ()(b)", @"\2:\1", "ab"));
    }

    [Fact]
    public void Inline_verbose_flags_restore_parent_scope_and_preserve_capture_numbers()
    {
        Assert.Equal("b:a", py.re.sub("(?x)(?P<n>a) # ignored (\n(b)", @"\2:\1", "ab"));
        Assert.Equal("b:a", py.re.sub("(?x:(?P<n>a) # ignored (\n)(b)", @"\2:\1", "ab"));
        Assert.Equal("b:a", py.re.sub("(?x:(?P<n>a)(?-x:#))(b)", @"\2:\1", "a#b"));
        Assert.Equal("c:b:a", py.re.sub("(?x:(?P<a>a)(?-x:(b)#)(?x: # ignored ) (\n(c)))", @"\3:\2:\1", "ab#c"));
        Assert.NotNull(py.re.match("(?x:(a))#(b)", "a#b"));
    }

    [Fact]
    public void Octal_replacements_are_not_confused_with_group_references()
    {
        Assert.Equal("a", py.re.sub("x", @"\141", "x"));
        Assert.Equal("\u00ff", py.re.sub("x", @"\377", "x"));
        Assert.Equal("\0" + "8", py.re.sub("x", @"\08", "x"));
        Assert.Equal("\u0001" + "8", py.re.sub("x", @"\018", "x"));
        Assert.Throws<ArgumentException>(() => py.re.sub("x", @"\400", "x"));
        Assert.Throws<ArgumentException>(() => py.re.sub("x", @"\777", "no match"));
        Assert.Equal("k8", py.re.sub("(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)", @"\118", "abcdefghijk"));
    }

    [Fact]
    public void Invalid_replacement_references_fail_even_without_matches()
    {
        Assert.Throws<ArgumentException>(() => py.re.sub("x", @"\2", "no match"));
        Assert.Throws<ArgumentException>(() => py.re.sub("x", @"\q", "no match"));
        Assert.Throws<ArgumentException>(() => py.re.sub("x", @"\g<missing>", "x"));
        Assert.Equal("\0", py.re.sub("x", @"\000", "x"));
    }
}
