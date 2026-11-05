# SqlGlotSharp.Extensions.Layer3

The Layer 3 expression dialect for [SqlGlotSharp](https://github.com/marcoparenzan/sqlglotsharp), the unofficial C# port of [SQLGlot](https://github.com/tobymao/sqlglot).

Layer 3 is the base SQLGlot dialect plus one multi-word keyword, `MEMBER OF`, which is parsed as `exp.JSONArrayContains`. It is not part of SQLGlot: it is built only on the public extension points of SqlGlotSharp (`CreateTokenizer`, `CreateParser`, `CreateGenerator`, `Dialect.register`). Generation is the base generator's, so `x MEMBER OF y` is written as `JSON_ARRAY_CONTAINS(x, y)`.

## Install

```powershell
dotnet add package SqlGlotSharp.Extensions.Layer3
```

It depends on the `SqlGlotSharp` package of the same SQLGlot version. Target: `net10.0`.

## Use

Register the dialect once at startup, then use the name `layer3` like any other dialect:

```csharp
using SqlGlotSharp;
using SqlGlotSharp.Extensions.Layer3;

Layer3.Register();                                         // registers the name "layer3"
var ast = SqlGlot.parse_one("'a' MEMBER OF t.tags AND b = 1", read: "layer3");
var sql = ast.sql("layer3");                               // JSON_ARRAY_CONTAINS('a', t.tags) AND b = 1
```

`Register()` is safe to call more than once. Registration is process-wide, as in SQLGlot.

## Limits

The right side of `MEMBER OF` is parsed as a column. The behaviour was written from the host's description and verified on the host corpus only (see `docs/OPENPOINTS.md` in the repository).

## License

MIT, Copyright (c) 2026 Marco Parenzan.
