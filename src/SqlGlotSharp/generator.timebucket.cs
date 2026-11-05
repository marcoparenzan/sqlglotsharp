namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        /// <summary>Extension nodes require a dialect-specific semantic contract, never Func fallback.</summary>
        public virtual string timebucket_sql(exp.TimeBucket expression) =>
            throw new NotPortedException("TimeBucket generation requires Timescale identity or a proven PostgreSQL DATE_BIN mapping");
    }
}
