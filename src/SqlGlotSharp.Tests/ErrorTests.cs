namespace SqlGlotSharp.Tests;

public class ErrorTests
{
    [Fact]
    public void Parse_errors_preserve_message_metadata_and_list_identity()
    {
        var error = errors.ParseError.@new("bad SQL", description: "bad", line: 2, col: 3, highlight: "x");
        Assert.Equal("bad SQL", error.Message);
        Assert.Equal("bad SQL", error.ToString());
        var detail = Assert.Single(error.errors);
        Assert.Equal(7, detail.Count);
        Assert.Equal(2, detail["line"]);
        Assert.Null(detail["into_expression"]);
        var list = new List<PyDict<string, object?>> { detail };
        Assert.Same(list, new errors.ParseError("bad", list).errors);
        var merged = errors.merge_errors([error, new errors.ParseError("bad", list)]);
        Assert.Equal(2, merged.Count);
        Assert.Same(detail, merged[0]);
    }

    [Fact]
    public void Token_error_preserves_optional_offsets_and_base_exception_type()
    {
        var error = new errors.TokenError("bad token", 1, 5);
        Assert.IsAssignableFrom<errors.SqlglotError>(error);
        Assert.Equal(1, error.start);
        Assert.Equal(5, error.end);
        Assert.Null(new errors.TokenError("bad").start);
    }

    [Fact]
    public void Highlights_sort_overlaps_without_losing_plain_context()
    {
        var result = errors.highlight_sql("SELECT foo bar", [(9, 11), (7, 9), (8, 8)], 3);
        Assert.Equal("CT ", result.start_context);
        Assert.Equal("foo b", result.highlight);
        Assert.Equal("ar", result.end_context);
        Assert.Equal("CT " + errors.ANSI_UNDERLINE + "foo" + errors.ANSI_RESET +
            errors.ANSI_UNDERLINE + " b" + errors.ANSI_RESET + "ar", result.formatted_sql);
        Assert.Throws<ArgumentException>(() => errors.highlight_sql("SELECT 1", []));
    }

    [Fact]
    public void Highlights_follow_python_code_point_indexes_for_unicode()
    {
        var result = errors.highlight_sql("a😀b", [(1, 1)], 1);
        Assert.Equal(("a" + errors.ANSI_UNDERLINE + "😀" + errors.ANSI_RESET + "b", "a", "😀", "b"), result);
    }

    [Fact]
    public void Concatenation_limits_messages_using_python_slice_semantics()
    {
        object?[] items = [new errors.ParseError("one"), "two", 3];
        Assert.Equal("one\n\ntwo\n\n... and 1 more", errors.concat_messages(items, 2));
        Assert.Equal("... and 3 more", errors.concat_messages(items, 0));
        Assert.Equal("one\n\ntwo\n\n... and 4 more", errors.concat_messages(items, -1));
        Assert.Equal("", errors.concat_messages([], 1));
    }
}
