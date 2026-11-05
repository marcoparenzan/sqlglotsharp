namespace SqlGlotSharp;

public static partial class exp
{
    /// <summary>Timescale bucket call. Optional overload arguments remain ordered and uninterpreted.</summary>
    public sealed class TimeBucket : Expression, Func
    {
        private static readonly PyDict<string, bool> Arguments = new() { ["width"] = true, ["this"] = true, ["expressions"] = false };
        private static readonly HashSet<string> Required = ["width", "this"];
        private static readonly List<string> Names = ["TIME_BUCKET"];
        public TimeBucket(PyDict<string, object?> args) : base(args) { }
        public TimeBucket(object? width = null, object? @this = null, List<object?>? expressions = null)
            : this(new PyDict<string, object?> { ["width"] = width, ["this"] = @this, ["expressions"] = expressions ?? [] }) { }
        public override string key => "timebucket";
        public override PyDict<string, bool> arg_types => Arguments;
        public override HashSet<string> required_args => Required;
        public override bool is_var_len_args => true;
        public override string var_len_arg_key => "expressions";
        public override List<string> _sql_names => Names;
    }
}
