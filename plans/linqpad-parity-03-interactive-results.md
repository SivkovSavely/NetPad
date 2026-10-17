# Plan 03 — Interactive results / live HTML controls

Paste-ready work package. Read master plan + plans 01/02 first. Depends on 01 (mutable slots, options) and 02 (Hyperlinq action-id pattern, panel routing, KeepRunning).

## Task

Build a reusable **C# object ↔ result DOM bridge** (generic live-control protocol), then implement the selected controls and `Util.Chart` on top. Central rule: one generic protocol; no per-control IPC systems.

## Current/relevant architecture

- Mutable slots: `ScriptOutput{OutputId, IsUpdate}` end-to-end (see master plan "Presentation pipeline"). Frontend slot replacement with binding-scope disposal: `output-pane/components/dump-container.ts` (`replaceOutput`, `bindingScopes`, `beforeAppendHtml` → `resultControls.bind(fragment)`).
- Event delegation precedent: `[data-on-demand-id]` click handler installed once per dump container.
- Host dispatch precedent: `ExpandOutputMessage` → `ScriptRunner.ExpandOutput` → `Util.ExpandOnDemand(id)`; registries cleared in `ScriptRunner.Run`.
- App→host typed requests: `ScriptsController` endpoints → `ClientServerScriptRunner` → `ScriptHostProcessManager.Send(...)`.
- O2Html converter pattern for presentational types (`Presentation/Html/UtilExtrasHtmlConverters.cs`).
- `ProgressBar` already demonstrates interactive in-place updates via OutputId.

## Required behavior / acceptance criteria

### Generic live-control protocol
One mechanism providing:
1. Stable control ids minted C#-side (`"CT" + guid N`), embedded as `data-netpad-control-id` on rendered root element (+ `data-netpad-control-type`).
2. Initial render = normal Dump of the control (converter renders current property state to HTML).
3. Property mutations after dump → slot update through `Sink.ResultWrite(node, options, outputId, isUpdate)` reusing the control's own output id so the DOM node is replaced/patched in place.
4. Events: single delegated listener per dump container (extend existing click handler; add `change`/`input` delegation) reading `data-netpad-*` attributes, sending ONE typed message family `ControlEventMessage{scriptRunId?, controlId, eventType, payloadJson}` frontend→app→host.
5. Host-side registry maps controlId → live Control instance + event handlers; stale ids ignored; cleared on rerun/clear (same place as OnDemandRegistry); disposal releases handlers and stops updates.
6. Payloads are JSON-serializable primitives only; never raw C#.
7. Before first dump, property changes just mutate pending state (no output writes).
8. Non-interactive sinks degrade to static render of current state (eager evaluation analog of OnDemand).

### Controls (LINQPad-compatible names/properties where practical)
- Base `Control`: children/content, css classes, styles, html attributes, enabled/visible; `Dump()` integration.
- `Button(text).Click += ...`; enabled; mutable text/style after dump.
- `TextBox`/`TextArea`: value get/set both directions (input/change events push value back into C# object), placeholder.
- `CheckBox(checked, label)`: change event; bidirectional.
- `SelectBox(items)`: items/options set, selected value(s), selection-changed event, option-set replacement.
- `Div`/`Span`: containers over arbitrary dumpable children.
- Interactive `Image`: do NOT break existing `NetPad.Media.Image`; new type placed to avoid ambiguity (e.g. `NetPad.Presentation.Controls.ImageControl` or LINQPad-namespace shim) supporting URI/data/path sources.
- `FlexBox(direction, children)`: row/column layout wrapper.
- `TabControl(tabs named, content)`, selected tab read/write, selection changed event, mutation after dump.
- `MarkdownViewer(markdown)`, `LatexViewer(latex)`: live source updates after dump; reuse plan 02 renderers.
- `IFrame(urlOrHtml)`: sandboxed (sandbox attribute, no app privileged API access).

### Util.Chart
High-level chart API rendering common sequence data (line/bar/column/scatter/pie), NetPad-native model serialized to the frontend; render via small native SVG renderer unless complexity forces one mature lib (justify if so). Charts participate in normal Dump, work inside DumpContainer slots and panels, resize sensibly (CSS), static fallback = data table or SVG snapshot in non-interactive output, live data/option updates when chart object mutated. **No EChart class.**

### Lifecycle
Results clear/rerun/close → dispose control registrations, stop event routing, release bindings, no leaked script object graphs (registry cleared + frontend binding scopes disposed via existing mechanisms). Property update must not duplicate DOM ids.

## Exact files/symbols

- Core: new `Presentation/Controls/` namespace — `Control.cs` base + each control; converters file(s) under `Presentation/Html/Controls*Converters.cs` or extend extras converters; `Util.Controls` factories or direct constructors (match LINQPad ergonomics); registry partial in `Util.Interactive.cs`.
- Messages: `ControlEventMessage`, possibly `ControlPropertyUpdateMessage` (prefer slot-update reuse instead).
- ScriptHost: `Program.cs`/`ScriptRunner.cs` handler + clearing.
- App: controller endpoint(s) for control events; frontend: extend `dump-container.ts` delegation (click/change/input), `result-controls.ts` binding for control elements, chart renderer component under `output-pane/components/` (e.g. `chart-view.ts`), KaTeX/markdown reuse from plan 02.
- Tests: core protocol unit tests (id stability, stale handling, serialization shape), per-control HTML/state tests, chart model tests; Jest specs for delegation/binding/stale-slot behavior where harness permits.

## Dependencies

Plan 01 (slots/options/composition recursion), Plan 02 (Hyperlinq action ids, Markdown/LaTeX renderers, panel routing, KeepRunning keeping callbacks alive).

## Compatibility/persistence semantics

Purely additive runtime APIs; no persisted settings. Old scripts unaffected; `Media.Image` unchanged.

## Constraints / non-goals

No DataGrid; no EChart; no debugger; don't expose app-shell privileged APIs through generic attributes/events; no per-control bespoke protocols; no large chart dependency unless justified.

## Focused regression tests

- Protocol: initial render contains stable id; post-dump property change emits single update with same OutputId; events dispatched to registered handler only; unknown/stale ids ignored; registry cleared between runs (unit-level with fake sink/gateway).
- Button click fires handler; TextBox two-way value sync; CheckBox/SelectBox events + option replacement; TabControl selection event; Div/Span nested composition with ordinary dumps inside.
- Markdown/LaTeX viewers update source after dump.
- IFrame sandbox attributes present.
- Chart: model→render data mapping, row/type variants, static fallback content.
- Frontend: delegated events carry correct control id/event/payload; replaced slot disposes old scope (extend existing patterns).

## Completion checklist

- [x] Generic bridge implemented and all listed controls built on it
- [x] Chart done without EChart
- [x] Lifecycle/disposal verified by tests
- [N] Master checklist intentionally left for central reconciliation in the merge worktree
- [x] Implementation notes, validation results, and user validation guidance recorded below

## Progress log

- Implemented the reusable control tree, stable control ids, mutable output-slot updates, delegated control events, ScriptHost registry/dispatch, clear/disposal lifecycle, all selected controls, and `Util.Chart` with a native SVG renderer and static table fallback. No commit created in this worktree.

## Implementation notes / integration decisions

1. `Control.Dump()` uses the existing `ScriptOutput.OutputId` / `IsUpdate` path. The root control's stable `CT...` id is also its output id; nested controls have independent event ids but mutate the root slot. No parallel output protocol was added.
2. `ControlEventMessage` is the only control-event message family. The frontend sends JSON primitives; multi-select values are carried as a JSON-encoded string so the message payload remains a JSON primitive. The host rejects non-primitive/oversized payloads and ignores unknown ids.
3. The existing delegated dump-container event pattern now handles `click`, `input`, and `change`; one set of listeners is installed per container. App routing is `PATCH /scripts/{id}/controls/events` → `ScriptEnvironment.SendControlEvent` → ScriptHost dispatch.
4. Registrations are created while rendering a control tree, refreshed when a mutable slot is rerendered, and invalidated on removed children, `Control.Dispose()`, `Util.ClearResults()`, run start, and run end. Disposing clears event delegates and collection subscriptions. `Util.KeepRunning()` from plan 02 controls whether a run remains alive for post-entrypoint callbacks.
5. Output updates retain the original named-panel route by re-entering `OutputRouting` with the panel captured at the control's initial dump. ClearResults invalidates only controls in the target panel (or main Results).
6. Static sinks render the current control state without registering callbacks; form controls are disabled. `ImageControl` is intentionally separate from `NetPad.Media.Image`. Generic HTML attributes reject inline event handlers, style attributes, `srcdoc`, and reserved `data-netpad-*` protocol attributes; `IFrame` always uses an empty sandbox and no app privileges.
7. `Util.Chart` accepts labeled `ChartDataPoint` values, numeric sequences, and selector-based sequences. Mutable data/type/title changes replace its normal output slot. GUI results render responsive native SVG for line/bar/column/scatter/pie; non-interactive sinks retain the data table fallback. No chart dependency or EChart compatibility class was added.
8. Controls are in `NetPad.Presentation.Controls`, added to default script namespaces for direct construction. `Util.Chart` remains the script-facing chart factory. The optional `ScriptRunId` field on `ControlEventMessage` is not populated because current `ScriptOutput` event DTOs do not carry a per-run id; globally unique control ids plus run-scoped registry clearing provide stale-id rejection.

## Focused regression tests and validation

- `InteractiveControlTests` covers stable ids and slot updates, pending pre-dump state, button/input/check/select events, single/multi-select synchronization, nested controls, tab updates, viewer updates, iframe sandboxing, chart fallback, static output, stale ids, disposal, and unsafe attributes.
- Frontend specs cover delegated primitive events and SVG chart rendering.
- `scripts/agent-test.sh src/Tests/NetPad.Runtime.Tests/NetPad.Runtime.Tests.csproj --filter "FullyQualifiedName~InteractiveControlTests"` — passed, 8/8.
- `scripts/agent-build.sh src/Apps/NetPad.Apps.ScriptHost/NetPad.Apps.ScriptHost.csproj` — passed, 0 errors.
- `scripts/agent-build.sh src/Apps/NetPad.Apps.App/NetPad.Apps.App.csproj` — passed, 0 errors.
- `scripts/agent-test.sh` — app/MCP/common/integration suites passed; Runtime.Tests had 888 passed and one existing .NET 7 SDK reference-assembly test failure. The full build also hit the existing OmniSharp.NET `KeyValuePair.Deconstruct` compile failure on this machine.
- `scripts/agent-jest.sh test/windows/main/panes/output-pane/components/dump-container.spec.ts test/windows/main/panes/output-pane/components/chart-view.spec.ts` — blocked because `jest` is not installed (`jest: command not found`); no package operation was run.

## User validation notes

In an interactive script, try:

```csharp
var button = new Button("Start");
var lease = Util.KeepRunning();
button.Click += (_, _) => { button.Text = "Clicked"; lease.Dispose(); };
button.Dump();

var text = new TextBox("initial") { Placeholder = "Type here" };
text.ValueChanged += (_, _) => text.Value.Dump("Current input");
text.Dump();

var tabs = new TabControl(new TabPage("One", "first"), new TabPage("Two", "second"));
tabs.SelectionChanged += (_, _) => tabs.SelectedTab.Dump("Selected tab");
tabs.Dump();

Util.Chart(new[] { 2d, 4d, 3d }, ChartType.Line).Dump();
```

The script remains alive until the button is clicked and its lease is released. Confirm form changes reach the script, property mutations update the original slot, charts render as SVG, and clearing Results or rerunning stops old controls from routing events.
