namespace SqlGlotSharp;

public static partial class exp
{
    // py: sqlglot/expressions/core.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    public abstract partial class Expr
    {
        public PyDict<string, object?> args;
        public Expr? parent;
        public string? arg_key;
        public int? index;
        public List<string>? comments;
        public DataType? _type;
        public PyDict<string, object?>? _meta;
        public long? _hash;

        // py: sqlglot/expressions/core.py::Expr.__init__ #5f0668da60
        protected Expr(PyDict<string, object?> args)
        {
            this.args = args;
            parent = null;
            arg_key = null;
            index = null;
            comments = null;
            _type = null;
            _meta = null;
            _hash = null;

            if (!is_primitive)
            {
                foreach (var (arg_key, value) in this.args)
                    _set_parent(arg_key, value);
            }
            if (this is TimeUnit)
                TimeUnit__init__helper();
        }

        public abstract void _set_parent(string arg_key, object? value, int? index = null);

        protected static PyDict<string, object?> constructor_args__helper(params (string key, object? value)[] values)
        {
            var args = new PyDict<string, object?>();
            foreach (var (key, value) in values)
                if (value is not System.Reflection.Missing) args[key] = value;
            return args;
        }
    }

    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression._set_parent #9e4f8f2b47
        public override void _set_parent(string arg_key, object? value, int? index = null)
        {
            if (value is Expr expression)
            {
                expression.parent = this;
                expression.arg_key = arg_key;
                expression.index = index;
            }
            else if (value is List<object?> values)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (values[i] is Expr v)
                    {
                        v.parent = this;
                        v.arg_key = arg_key;
                        v.index = i;
                    }
                }
            }
        }
    }
}
