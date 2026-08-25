# LINQPad Parity Master Plan

Long-lived feature-parity push toward modern LINQPad (including useful paid-tier functionality), keeping NetPad cross-platform, maintainable, and rebaseable onto upstream.

Rules of engagement (from `AGENTS.md`): work on `personal` (or `feat/*`/`fix/*` branches), keep changes additive, preserve existing script behavior, backward-compatible persistence, coherent behavioral commits, no mega-commits. Target is modern LINQPad **behavior**, not binary compatibility. Never copy/decompile LINQPad code; use public docs only to resolve signatures/semantics when needed.

## Baseline personal work this project builds on (do NOT reimplement)

Commits on `personal` (verify with `git log upstream/main..HEAD`; hashes may move on rebase):

| Commit | Provides |
|---|---|
| `9eb9f448` | LINQPad-compatible `Util` members: `Metatext`, `Highlight`, `WithStyle`, `Image`, `GetAsmPath`, `Cmd/CmdAsync`, `Pivot`, `ToExpando`, `ReadLine`, `Break`, `HorizontalRun`, `ProgressBar`, `OnDemand`, `Run(scriptPath)`, `CurrentQueryPath`. Types in `Presentation/UtilExtrasTypes.cs`, HTML converters in `Presentation/Html/UtilExtrasHtmlConverters.cs`. |
| `b4bd43f5` | Host wiring for `Util.OnDemand` / `Util.Run`: `ExpandOutputMessage` IPC, `Util.OnDemandRegistry`, app-side `ScriptsController.ExpandOnDemand` → `ClientServerScriptRunner` → ScriptHost `Program.cs` handler → `Util.ExpandOnDemand(outputId)`. |
| `6f74fa9a` | Frontend activation/rendering of on-demand values (`[data-on-demand-id]` click delegation in `output-pane/components/dump-container.ts`). |
| `28ac5e7d` | `ScriptOutput.OutputId` + `IsUpdate`, server-side `DumpContainer` (stable output slot), mutable output slots through all output writers, `ScriptOutputFoldBuffer` for headless folding, `ScriptEnvironmentIpcOutputWriter` mutation handling incl. dropped-order safety. |
| `6dd01767` | Frontend replacement of mutable slots (`replaceOutput`), disposal of old binding scopes (`bindingScopes` map). |
| `a43fc6a6` | OmniSharp process environment-variable propagation. |
| `e1f283f2` | Validation wrappers `scripts/agent-{build,test,jest}.sh`, AGENTS.md. |

## Established architecture facts (verified 2026-08)

### Presentation pipeline
- Script calls `Dump(...)` → static `DumpExtension.Sink` (`IDumpSink`: `ResultWrite(o, options[, outputId, isUpdate])`, `SqlWrite`). Default sink `NullDumpSink`; ScriptHost installs `ClientServerDumpSink` (in `ExecutionModel/ClientServer/ClientServerDumpSink.cs`).
- HTML rendering: `HtmlPresenter.SerializeToElement(object?, DumpOptions?, bool isError)` (`Presentation/Html/HtmlPresenter.cs`) wraps everything in `div.group`; title via `h6.title`; CSS classes; code highlighting attr; `data-destruct`. Uses O2Html `HtmlSerializer` with converter list registered in its static ctor; depth/collection limits from `PresentationSettings.GetConfigFileValues()` (`maxDepth`, `maxCollectionSerializeLength`) applied via `UpdateSerializerSettings`.
- Mutable output protocol = plain `ScriptOutput` records with `OutputId` + `IsUpdate` flowing through the normal writers: `ClientServerOutputHtmlWriter` (host side) → `ScriptOutputMessage` → app-side `ScriptEnvironmentIpcOutputWriter` (queues `ScriptOutputEmittedEvent` batches, per-run CTS, output-limit + dropped-order handling) → frontend `DumpContainer.appendOutput` (order queue) → `replaceOutput` by `data-output-id` with binding-scope swap/disposal.
- Headless/external path folds updates into initial slots via `Apps/NetPad.Apps.App/Services/ScriptOutputFoldBuffer.cs` (100 KB cap, orphan updates dropped); external process writers: `ExternalProcessOutput{Html,Json,Console}Writer`.
- Invariant: output order stays deterministic even for async updates; updates replace in place; frontend disposes old binding scopes.

**Any new mutable/live feature must reuse this OutputId mechanism — no parallel protocols.**

### Compiler/runtime
- User code becomes top-level statements merged with embedded partial `Program` bootstrapper (`ExecutionModel.ClientServer.EmbeddedCode.Program.cs`); namespaces injected by `ClientServerCSharpCodeParser`; compile via `CSharpCodeCompiler.CreateCompilation` (`OutputKind.ConsoleApplication`, `WithOverflowChecks(true)` already set, `WithAllowUnsafe(true)`, source generators already driven via `CSharpGeneratorDriver`, embedded PDBs). Permutations: as-is / `(expr).Dump();` / append `;`.
- `PreprocessorSymbols.For(optLevel, tfv)` → `NETPAD`, `DEBUG|RELEASE`+`TRACE`, `NET`, `NETx_0`, `NETx_0_OR_GREATER`; applied in `CodeAnalysisService.GetParseOptions`; OmniSharp gets `<DefineConstants>` via `AppOmniSharpServer.SetPreprocessorSymbolsAsync`.
- Cache/fingerprint: `Scripts/ScriptFingerprint` (code hash, namespaces hash, references hash w/ package version + file lastWrite, kind, TFM, opt level, UseAspNet, data connection id). Consumed by `ClientServerScriptRunner.Setup` in-memory cache and `External/DeploymentCache`.
- References: abstract `Reference` + `AssemblyFileReference`/`AssemblyImageReference`/`PackageReference`, polymorphic STJ via `JsonInheritanceConverter<Reference>` discriminator.
- OmniSharp: `AppOmniSharpServer` writes `script.csproj` + `Bootstrapper_Program.cs`/`User_Program.cs`/`Data_Connection_Program.cs`; reacts to config-changed events.
- `.netpad` format: `{id}\n{json}\n#Code\n{code}` via `Apps/NetPad.Apps.Common/Scripts/ScriptSerializer.cs` (`ScriptConfigData`).

### App/frontend
- Session restore is backend-driven: C# `Sessions/Session.cs` persists `session.openScripts`/`session.active` in `ITrivialDataStore`; `AppSetupAndCleanupBackgroundService` reopens them at startup. Tab order lives in localStorage (`tab-bar.${viewerHost.name}.viewables-order`). Monaco view states kept in-memory per `TextEditor` instance only.
- Viewer system: `WorkAreaService` → `ViewerHostCollection`/`ViewerHost` → `ViewerRegistry` (`canHandle(viewable)`) → viewers; base `ViewableObject`/`ViewableTextDocument`/`ViewableScriptDocument`.
- Actions/shortcuts: `BuiltinActionProvider` (Monaco actions), `shortcuts/builtin-shortcuts.ts` (`ShortcutIds`, fluent `Shortcut` builder, `configurable()`), `ShortcutManager` merges with `Settings.KeyboardShortcuts.Shortcuts` (`Configuration/KeyboardShortcutOptions.cs`).
- Settings: `Configuration/Settings.cs` (+ sibling option classes), REST `GET/PUT /settings`, `SettingsUpdatedEvent` pushed to frontend; generated TS mirror in `api.ts`.
- Scripts library root: `Settings.ScriptsDirectoryPath`; `FileSystemScriptRepository` enumerates recursively.
- Recents: `RecentScriptsService` (`ITrivialDataStore`) + `/session/recent*` endpoints + `RecentScriptsChangedEvent`.

## Plans and order

| Plan | Title | Depends on |
|---|---|---|
| [01](linqpad-parity-01-dump-presentation.md) | Dump & presentation parity (ToDump, DumpOptions, DumpContainer surface, composition helpers, Dif, DumpTell, async dumping, exception links) | — |
| [02](linqpad-parity-02-script-host-apis.md) | Script host/runtime APIs (Hyperlinq, Markdown/LaTeX, Util.JS, HtmlHead, result-host commands, named panels, KeepRunning, QueryCancelToken+Cancel-and-Execute, richer Util.Run, CSV, XHTML writer, spreadsheet, GetMyScripts, Uncapsulate, GetPassword, TransactionIsolationLevel) | 01 (mutable slots) |
| [03](linqpad-parity-03-interactive-results.md) | Interactive results: generic live-control bridge + controls + Util.Chart | 01, 02 |
| [04](linqpad-parity-04-script-composition.md) | #load, script references, My Extensions, snippets, generators, #:package/#:project, context symbols, checked arithmetic, Main1..Main9, xUnit mode | 01 (loaded ToDump hook seam); independent of 02/03 except loaded-source hooks feed 01's global hook |
| [05](linqpad-parity-05-editor-application-ux.md) | Editor/app UX: feature search, searchable settings, doc switcher, MRU, multi library roots, full-text search, arbitrary text files, MD/HTML preview, revert, resource monitor, bookmarks | mostly independent; uses 02 Markdown renderer for preview |
| [06](linqpad-parity-06-workspaces-sessions.md) | Workspaces/sessions/shelving/state restoration | 05 (generic files, bookmarks, undo journal) |
| [07](linqpad-parity-07-benchmarking-code-inspection.md) | BenchmarkDotNet harness, native/JIT disassembly view, decompilation on F12 + programmatic helpers | 03 (chart), 04 (#load-independent but uses refs/namespaces plumbing) |

Recommended execution order: 01 → 02 → 03 → 04 → 05 → 06 → 07. Plans 04 and 05 can run in parallel after 01 if desired.

## Cross-cutting requirements (apply to every plan)

1. **Output ordering invariant**: deterministic order even with async updates; updates replace in place; dispose old binding scopes/callback registrations; no duplicate DOM ids; ordinary sequential Dump unchanged.
2. **Compiler/cache participation**: anything changing effective compilation inputs (loaded files, script refs, extensions, directives, generators, arithmetic mode, script kind) must be part of `ScriptFingerprint` and invalidate OmniSharp state where its project view changes. Never fix staleness by disabling caching.
3. **Persistence**: backward-compatible schemas; absence of a value = previous behavior; workspace/UI state stays out of portable `.netpad` files unless it is execution semantics.
4. **GUI vs headless**: script APIs that need the GUI degrade gracefully or throw clear `NotSupportedException`/`InvalidOperationException` — never fake success. Reuse the `OnDemand` interactive-vs-eager pattern (`ProgressBar.IsInteractiveSink()` seam).
5. **Cross-platform**: no Windows-only assumptions without capability detection + clear unavailable messages.
6. **Dependencies**: prefer existing deps/platform APIs; justified additions listed per plan (BDN, markdown renderer, KaTeX, ILSpy decompiler, maybe one chart lib). No ECharts/EChart parity class ever.
7. **Shortcuts**: every new user-facing command registers through `BuiltinShortcuts`/`ShortcutIds` when comparable commands are configurable; avoid collisions.
8. **Tests**: focused regression tests per plan following existing conventions (`src/Tests/NetPad.Runtime.Tests/...`, `NetPad.Apps.App.Tests/...`, Jest suites under `src/Apps/NetPad.Apps.App/App/src/**/*.spec.ts`). No giant snapshot suites.

## Explicit non-goals (do not implement)

DataGrid/editable grid/database parity/SQL IntelliSense/schema explorer; debugger; AI/`Util.AI`/agent/chat; EChart public API or compatibility class; general npad/LPRun CLI parity, CSV CLI switches, compile-only CLI switches; credentials/password-manager overhaul beyond thin `Util.GetPassword`; NuGet security/vuln work; OAuth/proxy/package-source credential work; general plugin system; Git tracking; project/app export; LINQ-to-SQL; VB/F#; misc "Extreme" items not selected below.

Minimal shared runtime/CLI change allowed only where a selected feature needs it (e.g. GUI/CLI preprocessor symbol).

## Scope checklist with status

Legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[B]` blocked (with reason) · `[N]` intentionally not applicable/documented difference.

### Dump/presentation (plan 01) — COMPLETE at `8d607973` + `783e3a8d`
- [x] instance `ToDump()`
- [x] script-global/static `ToDump(object)` (via `Util.RegisterToDumpTransformer`; loaded-source auto-discovery lands with plan 04)
- [x] richer `DumpOptions` (MaxRows/MaxDepth/Expanded/IncludeMembers/ExcludeMembers/FormatStrings)
- [x] `DumpOptions.Default` / global dump defaults equivalent (`Util.DumpDefaults`)
- [x] complete public `DumpContainer` (Title/CssClasses/Options/AppendContent/ClearContent)
- [x] `HighlightIf`
- [x] `WithCssClass`
- [x] `WordRun`
- [x] `VerticalRun`
- [x] `WithHeading`
- [x] `Util.Dif`
- [x] `DumpTell`
- [x] `DumpAsync(IAsyncEnumerable<T>)`

### Script-facing/host APIs (plan 02)
- [ ] `Hyperlinq`
- [ ] `Util.Markdown`
- [ ] `Util.Latex`
- [ ] `Util.JS.Run`
- [ ] `Util.JS.Eval`
- [ ] `Util.HtmlHead`
- [ ] `Util.ClearResults`
- [ ] `Util.HideEditor`
- [ ] `Util.HideResults`
- [ ] `Util.AutoScrollResults`
- [ ] `Util.KeepRunning`
- [ ] `QueryCancelToken`
- [ ] Cancel-and-Execute behavior
- [ ] richer `Util.Run` with child-result/output capture
- [ ] `Util.TransactionIsolationLevel`
- [ ] `Util.ToCsvString`
- [ ] `Util.WriteCsv`
- [ ] `Util.CreateXhtmlWriter`
- [ ] `.ToSpreadsheet()`
- [ ] workbook/multiple sheets
- [ ] `Worksheet`
- [ ] `Cell`
- [ ] formulas
- [ ] `Util.GetMyScripts`
- [ ] `Uncapsulate()`
- [ ] `Util.GetPassword`
- [ ] named result panels
- [ ] `DumpToNewPanel` equivalent

### Interactive results (plan 03)
- [ ] generic live `Control`
- [ ] `Button`
- [ ] `TextBox`
- [ ] `TextArea`
- [ ] `CheckBox`
- [ ] `SelectBox`
- [ ] `Div`
- [ ] `Span`
- [ ] interactive `Image`
- [ ] `FlexBox`
- [ ] `TabControl`
- [ ] `MarkdownViewer`
- [ ] `LatexViewer`
- [ ] `IFrame`
- [ ] `Util.Chart`
- [ ] live C#↔DOM property updates
- [ ] live DOM→C# events/state
- [ ] lifecycle/disposal/stale callback handling
- [N] EChart — explicitly excluded
- [N] DataGrid — explicitly excluded

### Script composition (plan 04)
- [ ] `#load` `.cs`
- [ ] `#load` NetPad script where appropriate
- [ ] relative/absolute load paths
- [ ] wildcard loads
- [ ] recursive wildcard loads
- [ ] transitive loads/cycle handling
- [ ] diagnostics mapped to loaded source
- [ ] navigation into loaded source
- [ ] loaded `ToDump` support
- [ ] compile-time script references
- [ ] My Extensions
- [ ] user snippets
- [ ] snippet namespaces
- [ ] snippet assembly references
- [ ] snippet NuGet references
- [ ] source generators (note: `CSharpCodeCompiler` already drives generators from referenced assemblies — verify coverage incl. analyzer assets from packages, fingerprinting, isolation)
- [ ] `#:package`
- [ ] `#:project`
- [ ] GUI/CLI/headless preprocessor distinction
- [ ] `CMD` compatibility where appropriate
- [ ] query-wide checked arithmetic (note: compiler already compiles with `OverflowChecks(true)`; make it configurable per script defaulting to current behavior)
- [ ] `Main1`…`Main9`
- [ ] configurable commands/shortcuts for alternate entry points
- [ ] built-in xUnit query/test mode

### Editor/application UX (plan 05)
- [ ] global feature search
- [ ] searchable Settings
- [ ] searchable open-document switcher
- [ ] Recent Files / MRU
- [ ] additional script-library roots
- [ ] full-text search across script roots
- [ ] arbitrary text files/extensions
- [ ] Markdown live preview
- [ ] HTML live preview
- [ ] Revert File to Disk
- [ ] per-script CPU monitor
- [ ] per-script memory monitor
- [ ] ordinary bookmarks
- [ ] next/previous/clear bookmark
- [ ] numbered bookmarks 0–9

### Sessions/workspaces (plan 06)
- [ ] restore all saved open documents
- [ ] restore all unsaved open documents
- [ ] restore generic files
- [ ] restore tab ordering (currently localStorage; move into versioned workspace state)
- [ ] restore active document/viewer
- [ ] restore cursor/selection/view state
- [ ] preserve unsaved modifications to saved files
- [ ] disk-change conflict handling
- [ ] persist undo history
- [ ] persist redo history
- [ ] cap history size
- [ ] named workspaces
- [ ] workspace create/rename/switch/delete
- [ ] default workspace
- [ ] shelving
- [ ] automatic safe shelf during restart/exit when appropriate
- [ ] list/restore/delete shelves
- [ ] shelf conflict handling
- [ ] crash-safe/versioned persistence

### Benchmarking (plan 07)
- [ ] benchmark selected code command
- [ ] BenchmarkDotNet
- [ ] isolated benchmark process
- [ ] live mutable progress/results
- [ ] final statistics table
- [ ] chart
- [ ] cancellation
- [ ] current script references/framework integration

### Code inspection/decompilation (plan 07)
- [ ] native/JIT disassembly
- [ ] Code pane native view
- [ ] F12 external-symbol decompilation
- [ ] SourceLink/source preferred over decompilation
- [ ] cached generated decompiled documents
- [ ] programmatic C# decompilation helper
- [ ] programmatic IL helper where appropriate
- [ ] programmatic native/JIT disassembly helper
- [ ] navigation into loaded `.cs` source
- [N] debugger — excluded
- [N] AI-assisted anything — excluded

## Architectural decisions log

1. **Mutable outputs**: single protocol = `ScriptOutput.OutputId`/`IsUpdate` end-to-end (host writer → IPC → frontend slot replace). Live controls (plan 03) layer event delegation + typed request messages on top; they do not get their own output channel.
2. **ToDump** (final): script-global transformer first, then instance hooks chained across transformation results; interception via a first-position O2Html converter + converter-less fallback serializer; per-traversal reference marks + 16-step guard against cycles; hook errors render as per-node error groups. See plan 01 notes.
3. **DumpOptions precedence**: explicit call/container > `Util.DumpDefaults` (script-global, runtime-only) > NetPad Results settings (already applied via `PresentationSettings`/serializer limits) > built-ins. Unset members are `null` so they never override lower layers. Changing `DumpDefaults` never writes app Settings.
4. **Checked arithmetic**: `CSharpCodeCompiler` already sets `OverflowChecks(true)` globally; add per-script `ScriptConfig` flag persisted optionally; absent ⇒ true (current behavior preserved); LINQPad-style toggle exposed in script properties UI.
5. **Source generators**: generator driver already exists in `CSharpCodeCompiler`; plan 04 focuses on NuGet analyzer asset discovery, fingerprint inputs, diagnostics surfacing, isolation review.
6. **Workspace/session state**: versioned JSON documents in `ITrivialDataStore` (atomic write + backup rotation), separate save keys per workspace; localStorage tab-order migrates into it.
7. **Named panels** (plan 02): frontend dynamic tabs inside output pane backed by per-panel `DumpContainer`s; `ScriptOutput` gains optional panel routing field (additive, defaults null = main results); headless fold buffer flattens panels sequentially with headings.
8. **Live-control bridge** (plan 03): C# `Control` tree serialized as HTML with `data-netpad-control-id`; property mutations emit slot updates; DOM events delegated once per dump container (pattern of `[data-on-demand-id]`), forwarded via one typed IPC message carrying control id/event/payload JSON; ScriptHost dispatches to registered handlers; registries cleared on rerun/clear.

## Intentional compatibility differences from LINQPad

Documented per plan as discovered; master list:

- `Util.Run(scriptPath)` opens/runs in-app (existing fork behavior kept); richer child-runner API added alongside rather than replacing.
- Windows-centric capabilities (registry, WMI, some dialogs) are replaced by cross-platform equivalents with graceful unavailability.
- `EChart` deliberately absent; `Util.Chart` is NetPad-native.
- Grid/DataGrid absent; rich dumps remain read-only HTML tables.

## Completion protocol

When finishing any unit of work: tick checklist items here and in the relevant plan, record commit hashes next to the corresponding entries in that plan's "Progress log" section, leave concise user-validation instructions, continue to the next dependency-ready plan.
