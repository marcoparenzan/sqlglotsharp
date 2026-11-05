"""Generate expected parse/SQL results for Layer3 texts from the Python oracle (run with the oracle venv)."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / "python"))
sys.path.insert(0, str(Path(__file__).parent))
import sqlglot
from sqlglot import errors
from sqlglot.serde import dump
from oracle_plugins.layer3 import Layer3

data = root / "src/SqlGlotSharp.Extensions.Layer3.Tests/Layer3"
texts = json.loads((data / "texts.json").read_text(encoding="utf-8"))
out = []
for text in texts:
    try:
        node = sqlglot.parse_one(text, dialect=Layer3)
        out.append({"text": text, "class": type(node).__name__, "sql": node.sql(dialect=Layer3), "ast": dump(node)})
    except errors.TokenError:
        out.append({"text": text, "error": "TokenError"})
    except errors.ParseError:
        out.append({"text": text, "error": "ParseError"})
    except Exception as e:  # unexpected: recorded so the C# side must reproduce it
        out.append({"text": text, "error": type(e).__name__ + ": " + str(e)[:80]})

(data / "expected.json").write_text(json.dumps({"oracle": "sqlglot v30.27.0", "parses": out}, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(out), "entries;", sum("error" in x for x in out), "errors")
for x in out:
    if x["text"].startswith(("'R-25", "('L2-100", "CAST", "defect.id = 1;", "sql_variant", "VAR_POP", "DATE_TRUNC('day'", "process_measure.sampled_at < TIMESTAMP '2026-03-01")):
        print(x["text"][:60], "->", x.get("class"), x.get("sql", x.get("error")))
