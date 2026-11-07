"""Register new plugin-backed tools in every catalog, snapshot and README list (docs/ADDING_TOOLS.md steps 2-4, 7, 8).

Usage (from the repo root):
    python -I scripts/register-tools.py tools.json

tools.json is a list of {"command": "list_models", "handler": "ListModelsHandler", "read_only": true}
(add "destructive": true when the wrapper declares Destructive = true).
The MCP tool name is "nwd_" + command. Step 1 (handler), 5 (server wrapper), 6 (Program.cs for a new class)
and the ToolShellTests case stay manual. Re-running with an already registered command is a no-op for it.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TESTS = ROOT / "tests" / "Bimwright.Nwd.Tests"
READMES = ["README.md", "README.ja.md", "README.vi.md", "README.zh-CN.md"]


def edit(rel, fn):
    p = ROOT / rel
    text = p.read_text(encoding="utf-8")
    new = fn(text)
    if new != text:
        p.write_text(new, encoding="utf-8")


def insert_before(anchor, block):
    def fn(text):
        if anchor not in text:
            raise SystemExit(f"anchor not found: {anchor!r}")
        return text.replace(anchor, block + anchor, 1)
    return fn


def main(spec_path):
    specs = json.loads(pathlib.Path(spec_path).read_text(encoding="utf-8"))
    catalog = (ROOT / "src/shared/Infrastructure/NwdCommandCatalog.cs").read_text(encoding="utf-8")
    specs = [s for s in specs if f'new NwdCommandInfo("{s["command"]}",' not in catalog]
    if not specs:
        print("nothing to register")
        return

    edit("src/shared/Plugin/NwdCommandRegistry.cs", insert_before(
        "        Add(new SendCodeHandler());\n",
        "".join(f"        Add(new {s['handler']}());\n" for s in specs)))
    edit("src/shared/Plugin/NwdCommandRegistry.Names.cs", insert_before(
        '        "send_code",\n',
        "".join(f'        "{s["command"]}",\n' for s in specs)))
    edit("src/shared/Infrastructure/NwdCommandCatalog.cs", insert_before(
        '        new NwdCommandInfo("send_code", false),\n',
        "".join(f'        new NwdCommandInfo("{s["command"]}", {"true" if s["read_only"] else "false"}),\n' for s in specs)))

    # NwdCommandCatalogTests: expected names, and write commands flagged not read-only.
    def catalog_tests(text):
        names = "".join(f'"{s["command"]}",' for s in specs)
        text = text.replace('"send_code","run_baked_tool","apply_bake"\n    };',
                            names + '"send_code","run_baked_tool","apply_bake"\n    };', 1)
        writes = "".join(f'"{s["command"]}",' for s in specs if not s["read_only"])
        if writes:
            text = text.replace('foreach (var w in new[] { "open_file",', 'foreach (var w in new[] { ' + writes + '"open_file",', 1)
        return text
    edit("tests/Bimwright.Nwd.Tests/NwdCommandCatalogTests.cs", catalog_tests)

    # ToolAnnotationTests keeps an explicit list of destructive tools ("destructive": true in the spec).
    destructive = [f'"nwd_{s["command"]}"' for s in specs if s.get("destructive")]
    if destructive:
        def destructive_list(text):
            anchor = '"nwd_dismiss_bake_suggestion"'
            start = text.index(anchor)
            end = text.index("\n    };", start)
            return text[:end] + ",\n        " + ", ".join(destructive) + text[end:]
        edit("tests/Bimwright.Nwd.Tests/ToolAnnotationTests.cs", destructive_list)

    # Golden tool lists.
    counts = {}
    for name, include in (("tools-default.json", lambda s: True), ("tools-full.json", lambda s: True),
                          ("tools-read-only.json", lambda s: s["read_only"])):
        p = TESTS / "Golden" / name
        tools = json.loads(p.read_text(encoding="utf-8"))
        before = len(tools)
        tools += [f"nwd_{s['command']}" for s in specs if include(s) and f"nwd_{s['command']}" not in tools]
        p.write_text(json.dumps(tools, separators=(",", ":")), encoding="utf-8")
        counts[name] = (before, len(tools))

    full_old, full_new = counts["tools-full.json"]
    ro_old, ro_new = counts["tools-read-only.json"]

    def snapshot(text):
        text = text.replace(f"Assert.Equal({full_old}, ToolNamesFor", f"Assert.Equal({full_new}, ToolNamesFor")
        text = text.replace(f"Assert.Equal({full_old - 1}, ToolNamesFor(new NwdMcpConfig {{ EnableSendCode = false }})",
                            f"Assert.Equal({full_new - 1}, ToolNamesFor(new NwdMcpConfig {{ EnableSendCode = false }})")
        text = text.replace(f"Assert.Equal({ro_old}, ToolNamesFor", f"Assert.Equal({ro_new}, ToolNamesFor")
        return text
    edit("tests/Bimwright.Nwd.Tests/ToolsListSnapshotTests.cs", snapshot)
    edit("tests/Bimwright.Nwd.Tests/McpProtocolSmokeTests.cs", lambda t: t
         .replace(f"Assert.Equal({full_old}, tools.Count);", f"Assert.Equal({full_new}, tools.Count);")
         .replace(f"Assert.Equal(readOnly ? {ro_old} : {full_old}, tools.Count);", f"Assert.Equal(readOnly ? {ro_new} : {full_new}, tools.Count);"))

    # README permissions.allow lists: append read-only tools after the last nwd entry of the block.
    ro_tools = [f"nwd_{s['command']}" for s in specs if s["read_only"]]
    if ro_tools:
        for readme in READMES:
            def allow(text):
                entries = list(re.finditer(r'^( +)"mcp__nwd-mcp__(nwd_[a-z0-9_]+)"$', text, re.M))
                if not entries:
                    raise SystemExit(f"no allow list in {readme}")
                last = entries[-1]  # the final entry of the JSON list has no trailing comma
                sep = ",\n" + last.group(1)
                block = "".join(f'{sep}"mcp__nwd-mcp__{t}"' for t in ro_tools if f'"mcp__nwd-mcp__{t}"' not in text)
                return text[:last.end()] + block + text[last.end():]
            edit(readme, allow)

    edit("README.md", lambda t: re.sub(r"MCP-\d+%20tools", f"MCP-{full_new}%20tools", t)
         .replace(f"**{full_old} tools**. `--read-only` leaves **{ro_old}**.", f"**{full_new} tools**. `--read-only` leaves **{ro_new}**.")
         .replace(f"exactly **{ro_old} tools**", f"exactly **{ro_new} tools**"))

    print(f"registered {len(specs)} commands; full {full_old}->{full_new}, read-only {ro_old}->{ro_new}")


if __name__ == "__main__":
    main(sys.argv[1])
