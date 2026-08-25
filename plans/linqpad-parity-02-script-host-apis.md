# Plan 02 — Script host/runtime APIs and result-host commands

Paste-ready work package. Read `plans/linqpad-parity-00-master.md` (architecture + rules) and plan 01 first. Depends on plan 01's DumpOptions/DumpContainer surface.

## Task

Implement the remaining selected script-facing `Util`/host APIs. Prefer extending the existing IPC/message patterns (`OnDemand`, `Run`, input prompting, mutable outputs). One typed general host-command protocol where conventions allow; not one bespoke endpoint per trivial command.

## Current/relevant architecture

- IPC: ScriptHost `src/Apps/NetPad.Apps.ScriptHost/Program.cs` registers handlers (`ipc.On<ExpandOutputMessage>(...)`, `RunScriptMessage`, `ReceiveUserInputMessage`, dump/cache messages) on `StdioIpcGateway`; app side `ClientServerScriptRunner.AddScriptHostOnMessageReceivedHandlers`; app→host via `ScriptHostProcessManager.Send(...)`.
- App-side REST entry for interactive requests: `Controllers/ScriptsController.cs` (see `ExpandOnDemand(Guid id, [FromQuery] string outputId)` → runner → host message).
- Frontend output: `output-pane.ts` subscribes to `ScriptOutputEmittedEvent`s; `components/dump-container.ts` handles ordering, `[data-on-demand-id]` click delegation, slot replacement with binding-scope disposal; `results-view.ts` wraps it.
- Output writers (host side): `ExecutionModel/ClientServer/ClientServerOutputHtmlWriter.cs`, external variants under `ExecutionModel/External/Interface/`.
- `Util.Extras.cs`: `OnRequestRunScript` delegate pattern for app-capability callbacks (`Util.Run`), `ProgressBar.IsInteractiveSink()` seam for GUI-vs-headless degradation, `OnDemandRegistry` lifecycle cleared per run in `Apps/NetPad.Apps.ScriptHost/ScriptRunner.cs:Run`.
- Existing fork behavior: `Util.Run(scriptPath)` sends `RunScriptFromPathMessage(path)`; app opens+runs the script. Keep compatible.
- Excel export exists frontend-only at `output-pane/components/excel-export/excel-service.ts` (uses a JS excel lib) — plan 13 item needs a C#-side spreadsheet writer instead; reuse the same file-format library family already in package.json if feasible, else DocumentFormat.OpenXml as the one justified dependency.
- Data connections: generated DbContext code lives in data-connection program files (`Data_Connection_Program.cs` OmniSharp view; runtime augmentation under `NetPad.Runtime/DataConnection`/app Data connection services); `Util.TransactionIsolationLevel` must hook where the connection is created/opened.

## Required behavior / acceptance criteria

### 1. Hyperlinq
Type `Hyperlinq` (+ converters): URI link; link with display text; action link `new Hyperlinq(() => { ... }, "text")` executed in script host; navigation to local file/script with optional line/column. Frontend gets safe action id (`data-netpad-action-id` or reuse on-demand id pattern); click → typed request → correct ScriptHost dispatch → registered handler invoked; stale ids after rerun/clear fail silently/safely; exceptions surfaced as error dump; registry cleared per run (mirror `ClearOnDemandRegistry`). No executable C# crosses the wire.

### 2. Markdown
`Util.Markdown(string)` returning dumpable representation rendered to HTML. Use a mature renderer — prefer an existing npm lib already present; otherwise add `marked` (tiny) frontend-side and render server-side-neutral by dumping raw HTML produced... Decision: implement server-side rendering with a small managed pipeline only if trivially safe; preferred: dump a wrapper object whose frontend converter renders markdown client-side using the same renderer reused by plan 05 preview (single source of truth). Raw HTML inside markdown: sanitize or escape by default (document choice). Expose generated HTML property if useful.

### 3. LaTeX
`Util.Latex(string)` dumpable; live viewer in plan 03. Render via KaTeX npm package added to app assets (offline bundled, no CDN). Inline vs display mode flag.

### 4. Util.JS
`Util.JS.Run(script)` and `Util.JS.Eval<T>(expression)` executing in the result browser context of the correct script: new typed messages `JsEvalMessage{correlationId, code, timeoutMs}` host→app→frontend (via event), result returned through existing user-input-style reply channel; Eval returns JSON-serialized value to host; JS exceptions propagate as script-side exceptions; stale correlation ids rejected; cancellation/timeout supported; headless → clear NotSupportedException. Do not use app-global eval beyond the results iframe/window context.

### 5. Util.HtmlHead
Per-script head customization API (`AddCss(text)`, `AddCssLink(uri)`, `AddScriptLink(uri)`, `AddScript(code)`, `AddRaw(html)`): scoped to current script's results document; dedupe identical resources; deterministic lifecycle across reruns (cleared with results); works for external output window; no cross-script leaks.

### 6. Result host commands
`Util.ClearResults()`, `Util.HideEditor()`, `Util.HideResults()`, `Util.AutoScrollResults(bool)`:
- ClearResults clears current script's results without terminating; disposes stale controls/callbacks (reuse frontend clear path used on rerun); SQL tab untouched unless explicit overload says include.
- HideEditor/HideResults toggle pane visibility of current view; reversible via UI (existing pane toggles); state not persisted as user setting.
- AutoScrollResults sets follow-bottom behavior per running script; manual scroll-away pauses following while enabled (match LINQPad-ish behavior).
Implement as one `ResultHostCommand` IPC message family (typed enum + payload) consumed by output pane / main window; headless = graceful no-op with metatext notice.

### 7. Named result panels
`Util.DumpToNewPanel(name)` / panel handle object with `.Dump(...)`/`.Write(...)`: dynamic tabs beside Results/SQL within output pane; per-script lifecycle (cleared/disposed on rerun/close); panels support ordinary dumps, DumpContainer slots, Markdown/LaTeX, Hyperlinq, later live controls; external/headless flattens panels sequentially with headings (fold buffer extension). Routing: additive optional field on `ScriptOutput` (e.g. `PanelName`) — null = main results.

### 8. Util.KeepRunning
Lease-based lifetime retention: registration object/idempotent calls; cooperative wait after top-level completes (e.g. `TaskCompletionSource` released when all leases disposed or Terminate/cancel); integrates Cancel/Terminate; old run's registrations disposed before new run; live control callbacks stay usable while alive; no busy loops/leaked tasks.

### 9. QueryCancelToken + soft cancellation
Expose current run's CancellationToken: `Util.QueryCancelToken` (CancellationToken-compatible surface; LINQPad name if docs confirm). Each execution gets fresh token; Cancel button first requests cooperative cancellation (runner cancels token, waits grace period) then falls back to existing hard stop; **Cancel-and-Execute**: new app action/shortcut that requests cancel, awaits inactive status, starts requested run exactly once; never reuse cancelled token across runs.

### 10. Richer Util.Run
Keep existing open-and-run overload. Add child-runner abstraction: `var runner = await Util.RunAsync(path)` (and sync) returning handle with `await completion`, `Status/Succeeded/Failed`, captured stdout/results (via fold-buffer-like capture), `DumpToParent()` merge option, async APIs, cancellation, no cross-talk. Support script arguments where naturally expressible (persisted args passed via RunOptions). Must not deadlock when invoked from running script (fire-and-forget app-side orchestration; child runs in its own environment).

### 11. CSV APIs
`Util.ToCsvString(IEnumerable)` overloads + `Util.WriteCsv(source, path)`: anonymous objects/POCOs/dictionaries/expando/scalars sequences; RFC4180 quoting/escaping; stable headers (first-row shape for objects, dictionary keys union); null handling; delimiter option; invariant culture default with culture override; streaming write for WriteCsv.

### 12. XHTML writer
`Util.CreateXhtmlWriter()` returning writer with `Write(object)`/`ToString()` building standalone HTML doc reusing `HtmlPresenter` serialization; no second serializer.

### 13. Spreadsheet API
C#-side workbook model exposed to scripts: `.ToSpreadsheet()` extension creating workbook from sequence; workbook `.AddSheet(name)`, worksheet indexer/cells, cell values incl. formula strings stored un-evaluated; save to path/stream. Reuse/choose one mature writer dep (prefer what excel-service uses if it has a .NET sibling; else DocumentFormat.OpenXml). Basic number/date/string support.

### 14. Util.GetMyScripts
Enumerate all configured script-library roots (plan 05 may add roots; read settings each call): returns simple records (name/path/root/kind) via app service endpoint → host message (or direct filesystem enumeration host-side against settings snapshot — choose simplest that respects multiple roots once they exist).

### 15. Uncapsulate()
Fluent private-reflection wrapper: `.Uncapsulate()` extension + dynamic wrapper supporting fields/properties/indexers/methods/constructors any visibility instance/static; robust overload binding (exact > widening > params); inherited private members via base-type walk; generic methods best-effort; clear MissingMember/AmbiguousMatch errors; no unsafe tricks. Tests required.

### 16. Util.GetPassword
Thin adapter: named lookup via existing secrets (`Util.Secrets`/UserSecrets infrastructure); interactive masked prompt via existing RequestUserInput IPC channel (masked input flag if needed) when key absent; never log values.

### 17. Util.TransactionIsolationLevel
Enum property settable before DB work; applied at connection/command transaction creation point in data-connection execution model; unset ⇒ unchanged behavior; too-late set ⇒ clear InvalidOperationException; no DB UI work.

## Exact files/symbols

- Core runtime: new partials `Util.Interactive.cs` (Hyperlinq/JS/HtmlHead/result commands/panels/KeepRunning/QueryCancelToken), `Util.Data.cs` (CSV/XHTML/spreadsheet/GetMyScripts/TransactionIsolationLevel), `Util.Reflection.cs` (Uncapsulate); presentation types file(s) + converters; messages under `ExecutionModel/ClientServer/Messages/`; runner changes in `ClientServerScriptRunner(.Setup)?.cs` (soft cancel, keep-running lease, child-run capture), `ScriptHostProcessManager`.
- ScriptHost: `Program.cs` handlers, `ScriptRunner.cs` registry clearing.
- App: controllers (interactive endpoints), `ScriptEnvironmentIpcOutputWriter` (panel routing passthrough), `HeadlessScriptExecutionService`/fold buffer (panel flattening), output-pane TS (`output-pane.ts`, new `result-panel` components, command handling), api.ts regenerated types.
- Tests mirroring plan sections in `NetPad.Runtime.Tests` + app tests.

## Dependencies

Plan 01 (DumpOptions/DumpContainer/composition). Plan 03 consumes Hyperlinq action-id pattern and panel routing. GetMyScripts coordinates with plan 05 multi-root (read whatever roots exist; single root today).

## Compatibility/persistence semantics

- All additive `Util` members; no existing signatures change.
- `ScriptOutput.PanelName` optional/additive; old frontends unaffected (null).
- Script arguments for Util.Run: persisted only if user opts into script config; absence = current behavior.
- TransactionIsolationLevel unset = previous behavior exactly.

## Constraints / non-goals

No npad/LPRun CLI parity; no credentials overhaul; no EChart; no debugger; don't leak app-internal mutable objects into script process; don't log passwords; don't create bespoke protocol per command where a typed family fits.

## Focused regression tests

- Hyperlinq callback routing/staleness/disposal; URI/file variants converter HTML.
- JS bridge correlation/stale rejection/exception propagation (unit-level with fake gateway).
- HtmlHead dedupe/lifecycle.
- KeepRunning lease semantics (complete/release/cancel/re-run disposal).
- QueryCancelToken fresh-per-run; cancel-and-execute sequencing at runner level.
- Child Run capture/isolation/deadlock-free smoke.
- CSV escaping matrix (quotes, commas, newlines, nulls, delimiters, culture), headers stability, streaming.
- XHTML writer doc structure.
- Spreadsheet model basics (sheets/cells/formula preserved).
- Uncapsulate private/static/inherited/generic/ambiguous cases.
- GetPassword adapter (fake secret store + prompt channel).
- Panel routing fold/flattening.

## Completion checklist

- [ ] All selected items implemented with acceptance criteria
- [ ] Tests added/passing
- [ ] Master checklist updated + commits logged below
- [ ] User validation notes left here

## Progress log

(commits appended here as they land)
