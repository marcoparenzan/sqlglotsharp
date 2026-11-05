using System.Collections;

namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.depth #8784f87e63
        public abstract int depth { get; }
        // py: sqlglot/expressions/core.py::Expr.iter_expressions #aac88fb9a0
        public abstract IEnumerable<Expr> iter_expressions(bool reverse = false);
        // py: sqlglot/expressions/core.py::Expr.find #886a91bc3d
        public abstract Expr? find(Type[] expression_types, bool bfs);
        public Expr? find(params Type[] expression_types) => find(expression_types, true);
        public T? find<T>(bool bfs = true) where T : Expr => find([typeof(T)], bfs) as T;
        // py: sqlglot/expressions/core.py::Expr.find_all #ed0e3574c9
        public abstract IEnumerable<Expr> find_all(Type[] expression_types, bool bfs);
        public IEnumerable<Expr> find_all(params Type[] expression_types) => find_all(expression_types, true);
        public IEnumerable<T> find_all<T>(bool bfs = true) where T : Expr => find_all([typeof(T)], bfs).Cast<T>();
        // py: sqlglot/expressions/core.py::Expr.find_ancestor #0b2b1ff255
        public abstract Expr? find_ancestor(params Type[] expression_types);
        // py: sqlglot/expressions/core.py::Expr.parent_select #ff27022c8f
        public abstract Select? parent_select { get; }
        // py: sqlglot/expressions/core.py::Expr.same_parent #246526f4c3
        public abstract bool same_parent { get; }
        // py: sqlglot/expressions/core.py::Expr.root #6988a2c8b6
        public abstract Expr root();
        // py: sqlglot/expressions/core.py::Expr.walk #33cab6280e
        public abstract IEnumerable<Expr> walk(bool bfs = true, Func<Expr, bool>? prune = null);
        // py: sqlglot/expressions/core.py::Expr.dfs #c8aece03c6
        public abstract IEnumerable<Expr> dfs(Func<Expr, bool>? prune = null);
        // py: sqlglot/expressions/core.py::Expr.bfs #a838935e9e
        public abstract IEnumerable<Expr> bfs(Func<Expr, bool>? prune = null);
    }

    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.depth #15d169e1c9
        public override int depth
        {
            get { var result = 0; for (var node = parent; node is not null; node = node.parent) result++; return result; }
        }
        // py: sqlglot/expressions/core.py::Expression.iter_expressions #d0630eeca2
        public override IEnumerable<Expr> iter_expressions(bool reverse = false)
        {
            var values = reverse ? args.Values.Reverse() : args.Values;
            foreach (var value in values)
            {
                if (value is IList list)
                {
                    if (reverse)
                    { for (var i = list.Count - 1; i >= 0; i--) if (list[i] is Expr node) yield return node; }
                    else
                    { foreach (var item in list) if (item is Expr node) yield return node; }
                }
                else if (value is Expr node) yield return node;
            }
        }
        // py: sqlglot/expressions/core.py::Expression.find #2ce21c2014
        public override Expr? find(Type[] expression_types, bool bfs) => find_all(expression_types, bfs).FirstOrDefault();
        // py: sqlglot/expressions/core.py::Expression.find_all #fa57737c2a
        public override IEnumerable<Expr> find_all(Type[] expression_types, bool bfs)
        {
            foreach (var expression in walk(bfs))
                if (expression_types.Any(type => type.IsInstanceOfType(expression))) yield return expression;
        }
        // py: sqlglot/expressions/core.py::Expression.find_ancestor #c1c334bfdb
        public override Expr? find_ancestor(params Type[] expression_types)
        {
            var ancestor = parent;
            while (ancestor is not null && !expression_types.Any(type => type.IsInstanceOfType(ancestor))) ancestor = ancestor.parent;
            return ancestor;
        }
        // py: sqlglot/expressions/core.py::Expression.parent_select #0e363a8229
        public override Select? parent_select => find_ancestor(typeof(Select)) as Select;
        // py: sqlglot/expressions/core.py::Expression.same_parent #964599a4d2
        public override bool same_parent => parent?.GetType() == GetType();
        // py: sqlglot/expressions/core.py::Expression.root #db107158df
        public override Expr root()
        {
            Expr expression = this;
            while (expression.parent is not null) expression = expression.parent;
            return expression;
        }
        // py: sqlglot/expressions/core.py::Expression.walk #b8c014d5e1
        public override IEnumerable<Expr> walk(bool bfs = true, Func<Expr, bool>? prune = null) => bfs ? this.bfs(prune) : dfs(prune);
        // py: sqlglot/expressions/core.py::Expression.dfs #23658854ad
        public override IEnumerable<Expr> dfs(Func<Expr, bool>? prune = null)
        {
            var stack = new Stack<Expr>(); stack.Push(this);
            while (stack.TryPop(out var node))
            {
                yield return node;
                if (prune?.Invoke(node) == true) continue;
                foreach (var child in node.iter_expressions(reverse: true)) stack.Push(child);
            }
        }
        // py: sqlglot/expressions/core.py::Expression.bfs #2eb4d532a0
        public override IEnumerable<Expr> bfs(Func<Expr, bool>? prune = null)
        {
            var queue = new Queue<Expr>(); queue.Enqueue(this);
            while (queue.TryDequeue(out var node))
            {
                yield return node;
                if (prune?.Invoke(node) == true) continue;
                foreach (var child in node.iter_expressions()) queue.Enqueue(child);
            }
        }
    }
}
