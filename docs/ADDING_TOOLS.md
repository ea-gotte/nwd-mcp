# Adding a tool

Every Navisworks-backed tool is a plugin **handler** (runs inside Navisworks, net48) plus a server **wrapper** (MCP
schema, .NET 8). `nwd_resolve_ids` is the reference example for new full-API tools:
`src/shared/Handlers/ResolveIdsHandler.cs` + `src/server/Tools/ModelTools.cs`.

## Checklist (all eight places, or a test fails)

| # | File | What to add |
|---|---|---|
| 1 | `src/shared/Handlers/<Name>Handler.cs` | `INwdCommand` with `Name` (snake_case, no `nwd_` prefix) and `IsReadOnly`. Wrap in `#if NAVIS2022 \|\| … \|\| NAVIS2027`. |
| 2 | `src/shared/Plugin/NwdCommandRegistry.cs` | `Add(new <Name>Handler());` |
| 3 | `src/shared/Plugin/NwdCommandRegistry.Names.cs` | the command name |
| 4 | `src/shared/Infrastructure/NwdCommandCatalog.cs` | `new NwdCommandInfo("<name>", isReadOnly)` |
| 5 | `src/server/Tools/<Toolset>Tools.cs` | `[McpServerTool(Name = "nwd_<name>", ReadOnly, Destructive, Idempotent, OpenWorld = false)]` wrapper calling `_client.SendAsync("<name>", args)` |
| 6 | `src/server/Program.cs` | for a **new tool class only**: `Add("<toolset>", typeof(<Class>))` in `ResolveToolTypesForRegistration` **and** `if (toolType == typeof(<Class>)) return mcp.WithTools<<Class>>();` in `RegisterToolType` (otherwise the server dies at startup with "Unsupported MCP tool type") |
| 7 | `tests/Bimwright.Nwd.Tests/` | a `PluginCases` entry in `ToolShellTests` (pins the envelope), the name in `NwdCommandCatalogTests`, the three `Golden/tools-*.json` lists, the counts in `ToolsListSnapshotTests` and `McpProtocolSmokeTests` |
| 8 | `README*.md` (4 languages) | read-only tools go in the `permissions.allow` list (`ToolAnnotationTests` checks it) |

## Automating steps 2–4, 7 and 8

`python -I scripts/register-tools.py tools.json` with `[{"command": "...", "handler": "...Handler", "read_only": true}]`
updates the registry, names, catalog, catalog test, golden lists, tool counts and README allow lists.
You still write the handler, the wrapper, the `Program.cs` lines for a new class and the `ToolShellTests` case.
Derive new handlers from `DocumentCommand` (`NwJson.cs`): it resolves the document and maps `CommandFailure` to coded errors.

## Navisworks object identity

Managed wrappers (`TimelinerTask`, `GroupItem`, …) are new objects on every walk, so `ReferenceEquals`
between two walks is always false. Identify saved items and tasks by index path, GUID or key fields instead.
`ModelItem` equality does work across lookups. Navisworks also auto-adjusts some values, for example the other
date of a task when a range would invert, so validate inputs before assigning them.

## Version differences

The plug-in compiles once per year. When an API member is missing in older years (for example
`VariantData.IsInt64` and `Document.FileVersion` are not in 2024), gate it with `#if NAVIS2026 || NAVIS2027`,
or read it by reflection when it is called rarely. Build plugin-navis24 and plugin-navis26 before committing.

## Toolsets and profiles

Put the tool in the toolset for its area (`src/server/ToolsetFilter.cs`). Read tools go in the read half
(`clash`, `timeliner`, `model`, …). Anything that changes the document goes in the `_write` half, which
`--read-only` removes. Profiles (`core`, `visual`, `coordination`, `4d`, `qto`, `data`) expand to toolsets.
When you add a toolset, add it to a profile too.

## Shared building blocks

| Need | Use |
|---|---|
| Accept "which items" | `ItemScope.TryResolve(p, doc, out items, out unresolved, out error)`. Takes exactly one of `item_ids` (any id form), `set_id`, or `selection:"current"`. On the server side, build the args with `ModelTools.ScopeArgs(...)` and reuse `ModelTools.ScopeHelp` in the description. |
| Accept ids | `ItemRefResolver` handles `0:6:3`, `pid:<model>:<pathId>`, `nid:<model>:<id>` and `guid:<guid>`. Never hand-parse ids. |
| Return ids | `ItemIdEmitter.TryFrom(p, doc, …)` honours `id_mode` = `path`\|`pid`\|`nid`\|`guid` and falls back to path. Recommend `pid` in descriptions of multi-step tools. |
| Long lists | `PageRequest.TryFrom(p, …)`, then `page.Apply(seq)`. Return `page` as-is. Reuse `ModelTools.PageHelp` in the description. |
| Long walks | Check `ctx.PastDeadline` and return what you have, with a `truncated_by_deadline` flag. |
| Big results | `output=file` is handled server-side (`ResponseBudget`, `ResponseSpillWriter`). Validate it with `ResponseBudget.InvalidOutput(output)`. |
| Saved items (sets, viewpoints) | `SavedItemResolver`. Its ids are `id:<guid>` or `path:/escaped/path`. |
| Errors | `NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT" \| "NO_DOCUMENT" \| "NOT_FOUND", message, meta)`. Messages must not contain absolute paths (`ErrorSanitizer` scrubs them anyway). |

## Rules

- Pure logic (parsing, mapping, paging) goes in `src/shared/Infrastructure` **without** Navisworks types, so CI can test it.
  Link the file into `tests/Bimwright.Nwd.Tests/Bimwright.Nwd.Tests.csproj` and unit-test it.
- Handlers run on the Navisworks UI thread. Keep them bounded: page results, honour the deadline, and never block on I/O.
- Write tools say in their description what they change and how to undo it. Destructive ones (anything that
  persists to disk or cannot be reverted with another call) get `Destructive = true`.
- Build at least one plug-in year that is installed locally (`dotnet build src\plugin-navis26\…csproj`) before claiming a handler compiles.
  The server tests do not compile handlers.
