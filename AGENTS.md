# AGENTS.md — rules for the agent porting sqlglot to C#

> **Status (2026-11-05):** the port is complete against the corpus (26,487 / 26,487). These rules still govern any further work (for example realigning with a newer official release: follow `docs/UPSTREAM-SYNC.md`). Paths below are relative to the repository root: `python/`, `src/`, `src/tools/`, `docs/`. Where a rule names a step or a file that no longer exists, `docs/ROADMAP.md` is current; `docs/history/PORTING_PLAN.md` is the original (completed) plan.

You are porting **sqlglot** (Python, `python/`) to **C# / .NET 10**. Read this file fully
before every session. The step-by-step sequence is in [PORTING_PLAN.md](docs/history/PORTING_PLAN.md):
always work on the **first unfinished step** of that plan, nothing else.

## 1. The one principle

**The C# code mirrors the Python code, line by line. It is NOT idiomatic C#.**

The C# port must be updated every time upstream sqlglot changes. That is only cheap if a
reader can put a Python function and its C# twin side by side and see the same shape:
same file, same class, same method name, same parameter names, same order of statements,
same comments. Every "improvement" you make breaks that and costs time at every future sync.

So:

- Do **not** rename anything to PascalCase. `_parse_select` stays `_parse_select`,
  `select_sql` stays `select_sql`, `TRANSFORMS` stays `TRANSFORMS`, `this` stays `@this`.
- Do **not** refactor, merge, split, reorder, "simplify" or optimise ported code.
- Do **not** fix upstream bugs. Port them as they are, add `// py-bug?: <why>` and a line in
  `docs/OPENPOINTS.md`.
- Do **not** replace a Python algorithm with a .NET library call that "does the same"
  unless the conversion table below says so.
- Keep the Python comments and docstrings (docstring → `/// <summary>` with the same text).

## 2. Hard rules

1. Never edit anything under `python/`. It is the upstream clone, read-only.
2. Never edit generated files (`*.g.cs`) or the test corpus (`D:\clones\corpus\**`). Regenerate them
   with the tools in `src/tools/` instead.
3. Never change an expected value in a test to make it pass. A failing case is either fixed in
   the C# code or listed in `src/SqlGlotSharp.Corpus/known-exclusions.json` with a reason (Marco reviews that list).
4. Every ported Python function, method, property or class-level attribute carries an
   **anchor comment** on the line before it (format in §5). No anchor = not ported.
5. Something you cannot port yet: write the C# signature, body
   `throw new NotPortedException("sqlglot/parser.py::Parser._parse_foo");`, keep the anchor
   with hash `0000000000`, and move on. The build must always compile.
6. Target `net10.0` only. No multi-targeting. No third-party runtime dependencies in the
   library (sqlglot has none). Test projects may use xUnit.
7. Build output goes to `artifacts/` inside the repository (set in
   `Directory.Build.props`; git-ignored). Logs, diagnostic reports and scratch files go to
   `artifacts/{logs,reports,tmp}`; the versioned result snapshot is `docs/status/`. Never copy recorded corpus files into the repository.
8. Packages go only to `D:\Dev\NuGetLocalFeed`, version
   `<upstream x.y.z>-net10-preview.<n>` (e.g. `30.27.0-net10-preview.1`). New content = new `n`.
9. Do not publish anything, do not install global tools. If you need a tool, write it down in
   `docs/OPENPOINTS.md` and ask.
10. English for code, comments, docs and commit messages.
11. Report faithfully: in your end-of-session note say what passed, what failed, what you skipped.

## 3. Layout mirror

Approved bootstrap override: .NET 10; paths are relative to the repository root (the
former `csharp/` folder was merged into it). Source and solution are in root `src/`, tests in `src/SqlGlotSharp.Tests/`,
and Markdown documentation in `docs/` (README.md and AGENTS.md at root), Directory.Build.props at root. Python is root `python/`.
The no-third-party-runtime-dependencies and faithful-porting rules remain unchanged.

| Python | C# |
|---|---|
| package `sqlglot/parsers/` | namespace `SqlGlotSharp.parsers` (lowercase, same name) |
| module `sqlglot/parsers/tsql.py` | file `src/SqlGlotSharp/parsers/tsql.cs` (same relative path, same name) |
| module-level functions/constants of `tsql.py` | members of `public static partial class tsql` in that namespace |
| class `TSQLParser` defined in `tsql.py` | `public partial class TSQLParser` nested in `static partial class tsql` |
| `sqlglot/__init__.py` (`parse_one`, `transpile`, …) | `public static partial class sqlglot` in namespace `SqlGlotSharp` |
| package `sqlglot/expressions/*` (re-exported as `exp`) | **one** `public static partial class exp` (namespace `SqlGlotSharp`), split over `src/SqlGlotSharp/expressions/<module>.cs` |

Imports map to `using static`:

```python
from sqlglot import exp, parser
from sqlglot.helper import seq_get
from sqlglot.tokens import TokenType
```

```csharp
using SqlGlotSharp;                          // exp, sqlglot
using static SqlGlotSharp.helper;            // seq_get(...)
using static SqlGlotSharp.tokenizer_core;    // TokenType (defined in tokenizer_core.py)
using parser = SqlGlotSharp.parser;          // parser.Parser
```

So that `exp.Select`, `exp.select(...)`, `exp.DType.ARRAY`, `seq_get(args, 0)` read exactly as in Python.

A huge Python file maps to one C# file. If the C# file exceeds ~6000 lines you may split it
into `parser.cs`, `parser.2.cs`, … (all `partial`), cutting **only between methods, in Python
order**, and noting the cut in the file header.

## 4. Conversion table (Python → C#)

### 4.1 Names
- Identical to Python. C# keywords get `@`: `@this`, `@in`, `@is`, `@as`, `@default`,
  `@operator`, `@params`, `@base`, `@object`, `@string`, `@event`, `@fixed`, `@lock`,
  `@checked`, `@namespace`, `@override`, `@ref`, `@out`, `@char`, `@bool`, `@int` …
- `.editorconfig` suppresses the naming analyzers (IDE1006, CA1707, CA1716, CA1708, CS8981).
  Do not "fix" those warnings.

### 4.2 Types
| Python | C# |
|---|---|
| `str` | `string` |
| `int` | `long` (use `int` only for list/string indexes and lengths) |
| `float` | `double` |
| `bool` | `bool` |
| `None` / `Optional[X]` | `null` / `X?` (nullable reference types enabled) |
| `list[T]` | `List<T>` |
| `tuple[...]` fixed small | value tuple `(A, B)`; homogeneous variable → `List<T>` |
| `dict[K, V]` | `PyDict<K, V>` (runtime: insertion-ordered, has `get`, `pop`, `setdefault`, `items`, `copy`) |
| `set[T]` / `frozenset[T]` | `HashSet<T>` / `FrozenSet<T>` (`System.Collections.Frozen`) |
| `IntEnum` with `auto()` | C# `enum` starting at **1**, same member order |
| `str` Enum | C# `enum` + generated `ToValue()` / `FromValue()` |
| `t.Any`, expression arg values | `object?` |
| `Callable[[A, B], R]` | `Func<A, B, R>` / named delegate |
| `Iterator[T]` / generator with `yield` | `IEnumerable<T>` with `yield return` |

### 4.3 Classes
- `class Foo(Bar)` → `public partial class Foo : Bar`.
- `@property def x` → `public virtual T x { get { ... } }` (setter if Python has one).
- `@classmethod` called on the class only → `public static`. Overridden per subclass and called
  via `self.`/`cls.` → see "class-level attributes" below.
- `@staticmethod` → `public static`.
- `__eq__` / `__hash__` → `Equals` / `GetHashCode`; `__repr__` / `__str__` → `ToString`;
  `__iter__` → `IEnumerable`; `__getitem__` → indexer.
- **Class-level attributes overridden in subclasses** (`TRANSFORMS`, `KEYWORDS`, `FUNCTIONS`,
  `INDEX_OFFSET`, `NULL_ORDERING`, …) are read through `self.X`, so they become a virtual
  instance property backed by a static field:

```csharp
// py: sqlglot/generators/postgres.py::PostgresGenerator.TRANSFORMS #3f9a1c2b07
public static new readonly PyDict<Type, Transform> TRANSFORMS_ = py.merge(Generator.TRANSFORMS_, new()
{
    [typeof(exp.ArrayConcat)] = (self, e) => ...,
});
public override PyDict<Type, Transform> TRANSFORMS => TRANSFORMS_;
```

  Pure-data attributes (strings, numbers, bools, dicts/sets of strings/enums) are **generated**
  by `src/tools/gen_csharp.py` into `*.data.g.cs`; you only port attributes containing lambdas or code.

### 4.4 Expressions (`exp`)
- `Expr` + `Expression` (Python) → C# abstract base `exp.Expr` holding all members of both;
  `exp.Expression : Expr` is the concrete base. Every concrete class derives from it.
- Python **traits** (`@trait`: `Condition`, `Predicate`, `Func`, `AggFunc`, `Binary`,
  `Connector`, `Query`, `Selectable`, `DerivedTable`, `UDTF`, `DDL`, `DML`, `TimeUnit`,
  `IntervalOp`, `SubqueryPredicate`, `ColumnConstraintKind`, …) → **marker interfaces** with
  the same name. `isinstance(x, exp.Func)` → `x is exp.Func`.
- Trait methods/properties are hosted on `exp.Expr` as `virtual` members inside a region
  `#region trait Query` (first statement `py.require_trait<exp.Query>(this);`). This keeps
  dynamic dispatch and lets `select.where(...)` compile on a concrete type.
- A parameter/return annotated with a trait in Python is typed `exp.Expr` in C#.
- `arg_types`, `key`, `required_args`, `is_var_len_args`, `_sql_names` → generated metadata
  (`expressions/*.meta.g.cs`), never hand-written.
- `node.args` is a `PyDict<string, object?>` exactly as in Python (values: `Expr`,
  `List<object?>`, `string`, `bool`, `null`). Do **not** introduce typed properties for args.
- Constructors are generated with named optional parameters from `arg_types`:
  `exp.Cast(this=x, to=y)` → `new exp.Cast(@this: x, to: y)`.

Generated constructor convention: named `object?` parameters use
`[System.Runtime.InteropServices.Optional]`, so omitted values arrive as
`System.Reflection.Missing.Value`. A generated helper excludes only that sentinel,
retaining explicitly supplied nulls. Dictionary overloads forward kwargs through the
concrete base chain. This is .NET constructor support, not a ported Python member.
Trait-specific constructors not yet translated must fail with NotPortedException.

Nested public enum aliases (Python `DataType.Type = DType`, `Properties.Location = PropertiesLocation`)
map to nested static constant tables of the original enum type. C# cannot expose a public
nested type alias; this preserves values/assignment identity but not alias use as a type
annotation. Use `exp.DType` / `exp.PropertiesLocation` in C# type signatures.

### 4.5 Statements and idioms
| Python | C# |
|---|---|
| `if x:` (x not bool) | `if (py.truthy(x))` |
| `a or b` used as value | `py.or(a, b)` (use `a ?? b` only if `a` is a non-bool, non-collection, non-string reference) |
| `a and b` used as value | `py.and(a, b)` |
| `not x` (x not bool) | `!py.truthy(x)` |
| `x in d` (dict) / list / set / str | `d.ContainsKey(x)` / `.Contains(x)` / `.Contains(x)` / `s.Contains(x, StringComparison.Ordinal)` |
| `isinstance(x, (A, B))` | `x is A or B` |
| `x[-1]`, `x[i]` | `x[^1]`, `x[i]` |
| any slice `x[a:b]`, `x[a:]`, `x[::-1]` | `py.slice(x, a, b)` (clamps like Python; never use C# ranges for slices) |
| `len(x)` | `py.len(x)` or `.Count`/`.Length` |
| `f"{a}.{b!r}"` | `$"{a}.{py.repr(b)}"` |
| `s.upper()`, `s.lower()` | `s.ToUpperInvariant()`, `s.ToLowerInvariant()` |
| any string comparison / search | ordinal (`StringComparison.Ordinal`); never culture-sensitive |
| `re.match/search/fullmatch/sub/split` | `py.re.match/search/fullmatch/sub/split` (Python semantics; patterns via `py.re.compile`, cached static) |
| `str(float)` / `int(s)` / `float(s)` | `py.str(d)` / `py.@int(s)` / `py.@float(s)` |
| `for i, x in enumerate(xs)` | `foreach (var (i, x) in py.enumerate(xs))` |
| `zip`, `reversed`, `sorted`, `any`, `all` | `py.zip`, `py.reversed`, `py.sorted`, LINQ `Any`/`All` |
| `try/except E as e` | `try { } catch (E e) { }` |
| `raise X(msg) from e` | `throw new X(msg, e);` |
| `getattr(self, f"{k}_sql")` | `py.getattr(self, $"{k}_sql")` (reflection, only where Python does it) |
| module-level `logger = logging.getLogger("sqlglot")` | `public static py.logging.Logger logger = py.logging.getLogger("sqlglot");` in the module class, with its anchor; calls stay `logger.warning(...)`. No logger parameters or properties that Python does not have |
| `**kwargs` forwarding | `PyKwargs kwargs` (= `PyDict<string, object?>`) |
| `def f(*expressions, opt=..., **opts)` (builders) | anchored `f(object?[] expressions, opt = ...)`, plus an unanchored `f(params object?[] expressions)` that forwards with defaults; call the anchored one with named arguments from a subclass (an `override` is not an overload candidate). Parser `**opts` are not supported |
| trait method typed `self: Q -> Q`, not overridden (`Query.where`, `limit`, `order_by`, ...) | generic extension method `public static Q where<Q>(this Q self, ...) where Q : Expr` in `exp`, first statement `py.require_trait<Query>(self)`, so chains keep their concrete type |
| trait method overridden per class (`Query.select`) | virtual member of `Expr` in `#region trait`, covariant overrides (`public override Select select(...)`) |
| call to a module function from a method with the same name (`and_` inside `Expression.and_`) | qualify it: `exp.and_(...)` |
| `assert cond, msg` | `if (!(cond)) throw new InvalidOperationException(msg);` |
| `copy.deepcopy` | port of the Python code path actually used (usually `Expr.copy()`) |

If a Python construct is not in this table: pick the most literal translation, add it to the
table in this file in the same commit, and mention it in your session note.

## 5. Anchors

Line before every ported member:

```csharp
// py: sqlglot/parser.py::Parser._parse_select #a1b2c3d4e5
```

- Path relative to `python/`, then `::Class.member` (or `::function` at module level,
  `::Class` for the class itself).
- `#…` = first 10 hex chars of the hash printed by `python src/tools/port_status.py --hash <anchor>`.
  This is how the sync tool knows that the Python side has changed.
- One anchor per Python member. Never two C# members for one anchor, never one C# member for two
  anchors (if the conversion really needs a helper, name it `<member>__helper` and give it no anchor).

## 6. How to work a task

1. `python src/tools/port_status.py --next 10` prints the next members to port (Python order).
2. Read the Python member and its callers if unclear. Translate it with §4.
3. Add the anchor with the current hash.
4. `dotnet build` — must be 0 errors.
5. Run the tests of the current step (see the step in `docs/history/PORTING_PLAN.md`), e.g.
   `dotnet test src/SqlGlotSharp.Tests` and the Scoreboard (see `USAGE.md`).
6. If the step's tests are not all green, the task is not done unless the remaining failures
   need members not yet ported (they then fail with `NotPortedException`, which is fine).
7. Commit every 5–15 members: `port: parser.py _parse_select.._parse_where (12 members)`.
8. Stuck for more than ~3 attempts on one member: stub it (§2.5), write an `docs/OPENPOINTS.md`
   entry with the error, move on.

## 7. Session note

At the end of each session, append to `docs/history/PORTING_LOG.md`:
date · step · members ported · tests: passed/failed/not run per level · open points added.
