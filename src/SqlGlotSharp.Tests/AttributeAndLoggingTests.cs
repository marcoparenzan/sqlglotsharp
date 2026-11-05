namespace SqlGlotSharp.Tests;

public class AttributeAndLoggingTests
{
    public class Parent
    {
        public string inherited => "parent";
        public static string shared => "static";
    }
    public class Child : Parent
    {
        public object? nullable => null;
        public int field = 7;
        public string broken => throw new InvalidOperationException("getter failure");
        public void method() { }
    }

    [Fact]
    public void Getattr_preserves_null_and_reads_inherited_static_and_field_values()
    {
        var child = new Child();
        Assert.Equal("parent", py.getattr(child, "inherited"));
        Assert.Equal("static", py.getattr(typeof(Child), "shared"));
        Assert.Equal(7, py.getattr(child, "field"));
        Assert.Null(py.getattr(child, "nullable", "fallback"));
        Assert.Equal("fallback", py.getattr(child, "missing", "fallback"));
        Assert.Throws<MissingMemberException>(() => py.getattr(child, "missing"));
        Assert.Throws<InvalidOperationException>(() => py.getattr(child, "broken", "fallback"));
        Assert.Throws<NotPortedException>(() => py.getattr(child, "method"));
    }

    [Fact]
    public void Logging_matches_python_named_logger_levels_handlers_and_records()
    {
        var logger = py.logging.getLogger("sqlglot.tests.logging");
        Assert.Same(logger, py.logging.getLogger("sqlglot.tests.logging"));
        Assert.Same(py.logging.getLogger("sqlglot"), parser.logger);
        Assert.Same(parser.logger, generator.logger);
        var records = new List<py.logging.LogRecord>();
        var handler = new py.logging.CallbackHandler(records.Add);
        logger.addHandler(handler);
        try
        {
            // Effective level defaults to WARNING, as with Python's root logger.
            logger.debug("debug"); logger.info("info"); logger.warning("warning"); logger.error("error");
            var error = new InvalidOperationException("broken");
            logger.exception("exception", error);
            Assert.Equal(new[] { py.logging.WARNING, py.logging.ERROR, py.logging.ERROR }, records.Select(r => r.levelno));
            Assert.Equal("warning", records[0].getMessage());
            Assert.Equal("WARNING", records[0].levelname);
            Assert.Equal("AttributeAndLoggingTests", records[0].module);
            Assert.Same(error, records[2].exc_info);

            records.Clear();
            logger.setLevel(py.logging.DEBUG);
            logger.debug("debug");
            logger.disabled = true;
            logger.error("dropped");
            Assert.Equal(new[] { "debug" }, records.Select(r => r.msg));
        }
        finally
        {
            logger.removeHandler(handler);
            logger.disabled = false;
            logger.setLevel(py.logging.NOTSET);
        }
    }
}
