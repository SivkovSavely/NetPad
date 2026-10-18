# Plan 07 — Benchmarking, native code view, decompilation

Paste-ready work package. Read master plan first. Depends on plan 03 (chart) and plan 04 (references/namespaces plumbing, #load-independent). No debugger. No AI.

## Task

BenchmarkDotNet-powered "benchmark selection", native/JIT disassembly view in Code pane, F12 external-symbol decompilation with caching, programmatic inspect helpers, all reusing the existing Code pane / ICodeService patterns.

## Current/relevant architecture (verified)

- Code pane: `windows/main/panes/code-pane/` hosts Syntax Tree + IL views; IL flow via `ICodeService.getIntermediateLanguage` (app service → script assembly inspection); `IlView` demonstrates debounce/cancel/reload on script/config changes.
- Script assembly location: runner working dirs (`WorkingDirectory` `/root/script/{run}/{name}.dll`) — app side can resolve current run's assembly via environment/runner state.
- Compilation inputs available app-side: `Script.Config` (namespaces/references/TFM), dependency resolver outputs.
- Plan 01 leaves exception-link navigation hook; plan 04 adds loaded-source trees preferred over decompilation.
- Chart: plan 03 `Util.Chart` frontend renderer component.

## Required behavior / acceptance criteria

### 1. Benchmark selected code
Workflow: select code → action/shortcut (`netpad.action.benchmarkSelection`, configurable) → generate isolated benchmark harness project/source: user code wrapper class with `[Benchmark]` per selected member/selection, using current TFM/NuGet+assembly references/namespaces; run BenchmarkDotNet in separate process (`dotnet run`-style invocation of generated project or BDN's own process spawning); stream progress + live partial results into a mutable result slot (OutputId mechanism); final results table (Mean/Error/StdDev/Allocated etc. as BDN emits) rendered + chart via Util.Chart-style visualization; cancellation action kills process tree; temp artifacts under app cache dir cleaned after run (and on startup sweep); no state leaks into user script runtime. Selection referencing undeclared locals ⇒ harness compile error surfaced clearly (no elaborate dataflow capture; optional small Roslyn capture only if trivial).
BDN dependency added to app (not runtime lib injected into scripts).

### 2. Native/JIT disassembly view
New Code-pane tab beside Syntax Tree/IL: disassembly of methods from current script assembly. Isolated helper process performs JIT+disasm (options: .NET runtime JIT Disasm via `COMPlus_JitDisasm`-style env on a child runner, or clrmd-based stack + disassembler lib); show method boundaries/symbol names; syntax highlighting (custom lightweight asm highlighter or highlight.js lang); unsupported platform/arch ⇒ clear message; reload/cancel behavior mirrors IlView (script/config change triggers refresh prompt/debounce). No instruction decoding from scratch.

### 3. F12 external-symbol decompilation
OmniSharp definition pipeline interception: when definition target resolves to metadata/no local source: prefer real source (embedded PDB/SourceLink fetch if readily available via existing symbol infra) else decompile containing assembly/type/member via ILSpy `ICSharpCode.Decompiler` package (license-compatible) into a read-only generated document opened through viewer system positioned at symbol. Cache decompiled source keyed by assembly identity (name/version/path); invalidate on identity/path change; documents marked "decompiled"; Save disabled against them; loaded `.cs` sources always win.

### 4. Programmatic helpers
Script-facing inspection APIs (LINQPad names where clear): e.g. `Util.Decompile(Type | MethodBase | string assemblyPath)` returning C# source text dumpable/code-highlightable; IL variant reusing existing IL infrastructure; native/JIT variant where supported (else clear error object). Return values are strings/structured records, not live app objects.

### 5. Code-pane reuse
All views hosted by existing code pane architecture; reuse controller/service patterns, debouncing, cancellation from IlView; no separate inspection window.

## Exact files/symbols

- App C#: new `Services/Benchmarking/*` (harness generator, BDN runner process mgmt, output parser), `Services/Decompilation/*` (ICSharpCode.Decompiler wrapper + cache), native disasm helper project or `Tools/` console app invoked as process, controllers/endpoints for benchmark start/cancel + disasm requests, `ICodeService` extensions (`getDisassembly`, `decompileSymbol`).
- Frontend: code-pane new tab components (`native-view.ts`, maybe `decompiled-view.ts`), editor go-to-definition hook integrating OmniSharp locations (where currently handled) to route metadata targets through decompile service; benchmark action in `builtin-action-provider.ts`/shortcuts; chart reuse.
- Core runtime: `Util.Inspection.cs` partial for programmatic helpers (script-side calls app endpoints via existing host-command channel pattern from plan 02 where GUI-dependent; headless = operate directly on loaded assemblies when possible).
- Tests: harness generation golden-ish minimal tests (structure not snapshots); BDN output parser unit tests with sample console output fixtures; decompiler cache keying/invalidation; fallback decision matrix (source vs decompile); disasm platform gating logic; helper API shapes.

## Dependencies

Plan 03 chart (visualization), plan 02 host-command channel (programmatic helpers UX), plan 04 loaded-source preference. BDN + ICSharpCode.Decompiler are the two justified dependencies.

## Compatibility/persistence semantics

No persisted settings beyond shortcut registrations; caches under app cache directory with identity-keyed eviction; no changes to script execution.

## Constraints / non-goals

No debugger; no AI; no proprietary LINQPad code; don't run arbitrary disasm tooling inside GUI process; don't break IL view; Windows/Linux/macOS capability detection mandatory.

## Focused regression tests

Per-section above plus: benchmark cancellation terminates children; artifact cleanup; decompiled doc read-only enforcement; navigation positioning smoke.

## Completion checklist

- [x] Sections 1–5 implemented with criteria
- [x] Platform gating messages verified
- [x] Focused tests added/passing
- [x] Master checklist reconciled centrally; plan 07 commits logged below

## Progress log

- 2026-10-17: Implemented plan 07 after rebasing on latest `personal` (`e4d5dd99`). Added isolated BenchmarkDotNet selection runs with mutable output, cancellation and result charts; native/JIT disassembly and platform gating; F12 source/decompiled navigation with persistent identity-keyed cache; and `Util` decompile/IL/JIT helpers. Added focused backend/runtime/frontend tests. API client regenerated from Swagger; output-pane event call updated for the generated control-event signature.
  - `d6b9b06e` Implement benchmarking and code inspection
  - `d235189d` Persist decompiled source cache
  - Focused validation passed: App tests 66/66, `UtilCodeInspectionTests` 9/9, focused Jest 5 suites/12 tests. Swagger generation and `git diff --check` passed.
  - Broad validation remains limited by unrelated failures: full .NET tests hit the OmniSharp.NET `KeyValuePair.Deconstruct` compile error and a Runtime test requiring unavailable .NET 7 reference assemblies; App tests passed 66/66 and Runtime tests passed 912/913. Full Jest had 46/47 suites pass, with the existing chart-view fallback assertion failing. `tsc --noEmit` reports three existing errors in `scripts-list.ts`, `file-viewer.ts`, and `settings/window.ts`.
  - The central master plan was not edited, as required by the scoped-work instruction.

2026-10-18 integration reconciliation:

- Loaded source definitions continue to prefer source over decompilation. Loaded `.cs` paths under configured library roots now open in Plan 05's generic file viewer at the mapped symbol line; paths outside those roots remain visible in the read-only Code pane rather than bypassing the restricted file API. Added a focused navigation test.
- Loaded-source viewer integration commit: `67c2d4cc Open loaded sources in the generic viewer`.
- Full Jest passed (49 suites/504 tests), and frontend `npx tsc --noEmit --pretty false` passed. App tests passed 66/66. Full .NET tests passed 924 with 5 skipped and one failure due solely to missing .NET 7 reference/implementation assemblies; standalone `OmniSharp.NET` build is now clean. Earlier chart-fallback, OmniSharp, and three TypeScript failures are resolved.
- No GUI/manual benchmark, decompilation, or native-disassembly validation was performed; those remain user-level smoke scenarios, not test failures.
