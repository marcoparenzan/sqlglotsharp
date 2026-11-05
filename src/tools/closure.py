"""Compute a conservative static SQLGlot import closure without importing upstream.

Only Python's standard library is used. All imports, including TYPE_CHECKING and
function-local imports, are followed. Dynamic imports are reported, not guessed.
"""
from __future__ import annotations

import argparse
import ast
import json
from collections import deque
from pathlib import Path

SEEDS = (
    "sqlglot.dialects.postgres", "sqlglot.dialects.tsql", "sqlglot.dialects.fabric",
    "sqlglot", "sqlglot.serde",
)


def module_index(root: Path) -> dict[str, Path]:
    result = {}
    for path in sorted((root / "sqlglot").rglob("*.py")):
        parts = list(path.relative_to(root).with_suffix("").parts)
        if parts[-1] == "__init__":
            parts.pop()
        result[".".join(parts)] = path
    return result


def members(tree: ast.Module) -> list[dict]:
    """Top-level definitions and assignments, including conditional definitions."""
    result = []

    def visit(nodes):
        for node in nodes:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                result.append({"name": node.name, "kind": type(node).__name__, "line": node.lineno})
            elif isinstance(node, (ast.Assign, ast.AnnAssign)):
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                for target in targets:
                    for name in ast.walk(target):
                        if isinstance(name, ast.Name) and isinstance(name.ctx, ast.Store):
                            result.append({"name": name.id, "kind": "attribute", "line": node.lineno})
            else:
                # Do not recurse into functions/classes, but include module-level if/try blocks.
                for field in ("body", "orelse", "finalbody"):
                    child = getattr(node, field, [])
                    if isinstance(child, list):
                        visit(child)
                for handler in getattr(node, "handlers", []):
                    visit(handler.body)
    visit(tree.body)
    return sorted(result, key=lambda item: item["line"])


def dependencies(tree: ast.Module, module: str, package: bool, index: dict) -> tuple[list, list, list]:
    internal, external, dynamic = set(), set(), []

    def add(name):
        if name in index:
            internal.add(name)
        elif name and not (name == "sqlglot" or name.startswith("sqlglot.")):
            external.add(name)

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                add(alias.name)
        elif isinstance(node, ast.ImportFrom):
            if node.level:
                parts = module.split(".") if package else module.split(".")[:-1]
                if node.level > len(parts):
                    raise ValueError(f"Invalid relative import in {module}:{node.lineno}")
                base = ".".join(parts[:len(parts) - node.level + 1])
                if node.module:
                    base += "." + node.module
            else:
                base = node.module or ""
            add(base)
            for alias in node.names:
                candidate = f"{base}.{alias.name}"
                if candidate in index:
                    internal.add(candidate)
                # SQLGlot exposes expressions under the public alias exp.
                if base == "sqlglot" and alias.name == "exp" and "sqlglot.expressions" in index:
                    internal.add("sqlglot.expressions")
        elif isinstance(node, ast.Call):
            func = node.func
            if (isinstance(func, ast.Name) and func.id == "__import__") or (
                isinstance(func, ast.Attribute) and func.attr == "import_module"
            ):
                dynamic.append({"line": node.lineno, "expression": ast.unparse(node)})
    return sorted(internal), sorted(external), sorted(dynamic, key=lambda item: item["line"])


def closure(root: Path, seeds=SEEDS) -> dict:
    index = module_index(root)
    pending = deque(seeds)
    seen, records = set(), {}
    while pending:
        module = pending.popleft()
        if module in seen:
            continue
        if module not in index:
            raise ValueError(f"Seed or dependency not found: {module}")
        seen.add(module)
        path = index[module]
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        imports, external, dynamic = dependencies(tree, module, path.name == "__init__.py", index)
        records[module] = {
            "path": path.relative_to(root).as_posix(), "members": members(tree),
            "imports": imports, "external_imports": external, "dynamic_imports": dynamic,
        }
        pending.extend(imports)
    return {
        "schema_version": 1, "analysis": "conservative-static",
        "seeds": list(seeds), "modules": sorted(records),
        "details": {name: records[name] for name in sorted(records)},
        "module_count": len(records),
        "member_count": sum(len(record["members"]) for record in records.values()),
    }


def main():
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream", type=Path, default=repo / "python")
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("scope.json"))
    parser.add_argument("--seed", action="append", help="Override seeds (repeatable)")
    args = parser.parse_args()
    result = closure(args.upstream.resolve(), args.seed or SEEDS)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{result['module_count']} modules; {result['member_count']} top-level members -> {args.output}")


if __name__ == "__main__":
    main()
