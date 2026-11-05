namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/temporal.py::DateTrunc.__init__ #03bf81f184
        protected static PyDict<string, object?> DateTrunc__init__helper(PyDict<string, object?> constructorArgs)
        {
            // Python **args creates a fresh mapping. Normalize before Expr attaches parents.
            var args = new PyDict<string, object?>();
            foreach (var (key, value) in constructorArgs) args[key] = value;
            var unabbreviate = args.get("unabbreviate", true);
            args.Remove("unabbreviate");
            var unit = args.get("unit");
            if (unit is Column or Literal or Var && !(unit is Column column && column.parts.Count != 1))
            {
                var unitName = py.upper(((Expr)unit).name);
                if (py.truthy(unabbreviate) && TimeUnit_UNABBREVIATED_UNIT_NAME__helper.TryGetValue(unitName, out var expanded))
                    unitName = expanded;
                args["unit"] = new Literal(@this: unitName, is_string: true);
            }
            return args;
        }
    }
}
