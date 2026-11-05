using System.Text;

namespace SqlGlotSharp.optimizer;

// py: sqlglot/optimizer/simplify.py::gen / Gen
/// <summary>Simple pseudo SQL generator for quickly generating sortable and unique strings.</summary>
public sealed class SimplifyGen
{
    private List<object?> stack = new();
    private readonly List<string> sqls = new();

    public static string gen(exp.Expr expression, bool comments = false) => new SimplifyGen().Gen(expression, comments);

    // Python repr() of a plain string
    private static string Repr(string text)
    {
        var quote = text.Contains('\'') && !text.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder();
        sb.Append(quote);
        foreach (var ch in text)
        {
            if (ch == quote || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < 0x20 || ch == 0x7f) sb.Append($"\\x{(int)ch:x2}");
            else sb.Append(ch);
        }
        sb.Append(quote);
        return sb.ToString();
    }

    public string Gen(exp.Expr expression, bool comments = false)
    {
        stack = [expression];
        sqls.Clear();

        while (stack.Count > 0)
        {
            var node = stack[^1];
            stack.RemoveAt(stack.Count - 1);

            if (node is exp.Expr expr)
            {
                if (comments && expr.comments is { Count: > 0 }) stack.Add($" /*{string.Join(",", expr.comments)}*/");

                if (!Handle(expr))
                {
                    if (expr is exp.Func) Function(expr);
                    else
                    {
                        var key = expr.key.ToUpperInvariant();
                        stack.Add(Args(expr) ? $"{key} " : key);
                    }
                }
            }
            else if (node is List<object?> list)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var n = list[i];
                    if (n is not null)
                    {
                        stack.Add(n);
                        stack.Add(",");
                    }
                }
                if (list.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else if (node is not null) sqls.Add(py.str(node));
        }

        return string.Concat(sqls);
    }

    private void Extend(params object?[] items) => stack.AddRange(items);

    private void Binary(exp.Expr e, string op) => Extend(e.args.get("expression"), op, e.args.get("this"));

    private void Unary(exp.Expr e, string op) => Extend(e.args.get("this"), op);

    private void Function(exp.Expr e) => Extend(")", e.args.Values.ToList(), "(", e.sql_names().First());

    private bool Args(exp.Expr node, int arg_index = 0)
    {
        var kvs = new List<object?>();
        var arg_types = node.arg_types.Keys.Skip(arg_index);

        foreach (var k in arg_types)
        {
            var v = node.args.get(k);
            if (v is not null) kvs.Add(new List<object?> { $":{k}", v is string s ? Repr(s) : v });
        }
        if (kvs.Count > 0)
        {
            stack.Add(kvs);
            return true;
        }
        return false;
    }

    private static string Quote(string text) => $"\"{text.Replace("\"", "\"\"")}\"";

    private bool Handle(exp.Expr e)
    {
        switch (e.key)
        {
            case "add": Binary(e, " + "); return true;
            case "alias": Extend(e.args.get("alias"), " AS ", e.args.get("this")); return true;
            case "and": Binary(e, " AND "); return true;
            case "anonymous":
            {
                var @this = e.args.get("this");
                string name;
                if (@this is string str) name = str.ToUpperInvariant();
                else if (@this is exp.Identifier id)
                {
                    name = (string)id.args["this"]!;
                    name = py.truthy(id.args.get("quoted")) ? Quote(name) : name.ToUpperInvariant();
                }
                else throw new ArgumentException($"Anonymous.this expects a str or an Identifier, got '{@this?.GetType().Name}'.");
                Extend(")", e.args.get("expressions") ?? new List<object?>(), "(", name);
                return true;
            }
            case "between": Extend(e.args.get("high"), " AND ", e.args.get("low"), " BETWEEN ", e.args.get("this")); return true;
            case "boolean": stack.Add(py.truthy(e.args.get("this")) ? "TRUE" : "FALSE"); return true;
            case "bracket":
                Args(e, 2);
                Extend("]", e.args.get("expressions") ?? new List<object?>(), "[", e.args.get("this"));
                return true;
            case "column":
                foreach (var p in ((exp.Column)e).parts.AsEnumerable().Reverse()) Extend(p, ".");
                stack.RemoveAt(stack.Count - 1);
                return true;
            case "datatype":
                Args(e, 1);
                stack.Add($"{e.args["this"]} ");
                return true;
            case "div": Binary(e, " / "); return true;
            case "dot": Binary(e, "."); return true;
            case "eq": Binary(e, " = "); return true;
            case "from": Extend(e.args.get("this"), "FROM "); return true;
            case "gt": Binary(e, " > "); return true;
            case "gte": Binary(e, " >= "); return true;
            case "identifier":
            {
                var text = (string)e.args["this"]!;
                stack.Add(py.truthy(e.args.get("quoted")) ? Quote(text) : text);
                return true;
            }
            case "ilike": Binary(e, py.truthy(e.args.get("negate")) ? " NOT ILIKE " : " ILIKE "); return true;
            case "in":
                stack.Add(")");
                Args(e, 1);
                Extend("(", " IN ", e.args.get("this"));
                return true;
            case "intdiv": Binary(e, " DIV "); return true;
            case "is": Binary(e, py.truthy(e.args.get("negate")) ? " IS NOT " : " IS "); return true;
            case "like": Binary(e, py.truthy(e.args.get("negate")) ? " NOT Like " : " Like "); return true;
            case "literal":
            {
                var text = (string)e.args["this"]!;
                stack.Add(e.is_string ? $"'{text.Replace("'", "''")}'" : text);
                return true;
            }
            case "lt": Binary(e, " < "); return true;
            case "lte": Binary(e, " <= "); return true;
            case "mod": Binary(e, " % "); return true;
            case "mul": Binary(e, " * "); return true;
            case "neg": Unary(e, "-"); return true;
            case "neq": Binary(e, " <> "); return true;
            case "not": Unary(e, "NOT "); return true;
            case "null": stack.Add("NULL"); return true;
            case "or": Binary(e, " OR "); return true;
            case "paren": Extend(")", e.args.get("this"), "("); return true;
            case "sub": Binary(e, " - "); return true;
            case "subquery":
                Args(e, 2);
                if (e.args.get("alias") is exp.Expr alias) stack.Add(alias);
                Extend(")", e.args.get("this"), "(");
                return true;
            case "table":
                Args(e, 4);
                if (e.args.get("alias") is exp.Expr table_alias) stack.Add(table_alias);
                foreach (var p in generators.dialect_helpers.table_parts((exp.Table)e).AsEnumerable().Reverse()) Extend(p, ".");
                stack.RemoveAt(stack.Count - 1);
                return true;
            case "tablealias":
            {
                var columns = e.args.get("columns") as List<object?>;
                if (columns is { Count: > 0 }) Extend(")", columns, "(");
                Extend(e.args.get("this"), " AS ");
                return true;
            }
            case "var": stack.Add(e.args.get("this")); return true;
        }
        return false;
    }
}
