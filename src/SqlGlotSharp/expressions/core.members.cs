using static SqlGlotSharp.helper;

namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expression.this #adac0ae9e2
        public virtual object? @this => args.get("this");
        // py: sqlglot/expressions/core.py::Expression.expression #798c775632
        public virtual object? expression => args.get("expression");
        // py: sqlglot/expressions/core.py::Expression.expressions #2fa665ba6f
        public virtual List<object?> expressions => py.or(args.get("expressions") as List<object?>, [])!;

        // py: sqlglot/expressions/core.py::Expression.text #05d681981f
        public virtual string text(string key)
        {
            var field = args.get(key);
            if (field is string s) return s;
            if (field is Identifier or Literal or Var) return (string)((Expr)field).@this!;
            if (field is Star or Null) return ((Expr)field).name;
            return "";
        }

        // py: sqlglot/expressions/core.py::Expression.name #e038272c57
        public virtual string name => text("this");

        // py: sqlglot/expressions/core.py::Expression.set #d98485f79d
        public virtual void set(string arg_key, object? value, int? index = null, bool overwrite = true)
        {
            Expr? node = this;
            while (py.truthy(node) && node!._hash is not null)
            {
                node._hash = null;
                node = node.parent;
            }
            if (index is not null)
            {
                var expressions = py.or(args.get(arg_key) as List<object?>, [])!;
                if (seq_get(expressions, index.Value) is null) return;
                var position = index.Value < 0 ? expressions.Count + index.Value : index.Value;
                if (value is null)
                {
                    expressions.RemoveAt(position);
                    foreach (var v in py.slice(expressions, index.Value))
                        ((Expr)v!).index = ((Expr)v).index - 1;
                    return;
                }
                if (value is List<object?> values)
                {
                    expressions.RemoveAt(position);
                    var insertion = Math.Clamp(index.Value < 0 ? expressions.Count + index.Value : index.Value, 0, expressions.Count);
                    expressions.InsertRange(insertion, values);
                }
                else if (overwrite) expressions[position] = value;
                else expressions.Insert(Math.Clamp(index.Value < 0 ? expressions.Count + index.Value : index.Value, 0, expressions.Count), value);
                value = expressions;
            }
            else if (value is null)
            {
                args.pop(arg_key, null);
                return;
            }
            args[arg_key] = value;
            _set_parent(arg_key, value, index);
        }

        #region trait TimeUnit
        // py: sqlglot/expressions/core.py::TimeUnit.__init__ #1c65e1e6e0
        protected void TimeUnit__init__helper()
        {
            py.require_trait<TimeUnit>(this);
            // super().__init__ has already run through the concrete constructor chain.
            var unit = args.get("unit");
            if (py.truthy(unit) && TimeUnit_VAR_LIKE__helper.Contains(unit!.GetType()) &&
                !(unit is Column column && column.parts.Count != 1))
            {
                var unit_name = ((Expr)unit).name;
                unit = new Var(@this: py.or(TimeUnit_UNABBREVIATED_UNIT_NAME__helper.get(unit_name), unit_name)!.ToUpperInvariant());
                args["unit"] = unit;
                _set_parent("unit", unit);
            }
            else if (unit?.GetType().Name == "Week")
            {
                var week = (Expr)unit;
                week.set("this", new Var(@this: ((Expr)week.@this!).name.ToUpperInvariant())); // type: ignore[union-attr]
            }
        }

        // py: sqlglot/expressions/core.py::TimeUnit.unit #33131581f7
        public virtual Expr? unit
        {
            get { py.require_trait<TimeUnit>(this); return args.get("unit") as Expr; }
        }
        #endregion
    }

    public partial class Column
    {
        /// <summary>Return the parts of a column in order catalog, db, table, name.</summary>
        // py: sqlglot/expressions/core.py::Column.parts #b489963b49
        public virtual List<Expr> parts => new[] { "catalog", "db", "table", "this" }
            .Where(part => py.truthy(args.get(part))).Select(part => (Expr)args[part]!).ToList();
    }
    public partial class Star
    {
        // py: sqlglot/expressions/core.py::Star.name #b970d69e3c
        public override string name => "*";
    }
    public partial class Null
    {
        // py: sqlglot/expressions/core.py::Null.name #7d915c98ac
        public override string name => "NULL";
    }
    public partial class Dot
    {
        // py: sqlglot/expressions/core.py::Dot.name #54a03b7bab
        public override string name => ((Expr)expression!).name;
    }
}
