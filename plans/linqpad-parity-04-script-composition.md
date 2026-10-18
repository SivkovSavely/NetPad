# Plan 04 — Script composition and mini-project capabilities

Paste-ready work package. Read master plan (compiler/runtime section) first. Mostly independent of plans 02/03; uses plan 01's global ToDump seam for loaded hooks.

## Task

The compiler/project-model plan: `#load`, navigation/IntelliSense for loaded source, compile-time script references, My Extensions, user snippets, source generators, `#:package`/`#:project`, context preprocessor symbols, query-wide checked arithmetic, alternate entry points `Main1`..`Main9`, built-in xUnit mode. Extend existing systems (`ScriptConfig`, `Reference` hierarchy, `ScriptCompiler`, dependency resolution, fingerprint cache, OmniSharp project setup); never create a parallel compilation system.

## Current/relevant architecture (verified)

- Pipeline: `ClientServerScriptRunner.Setup` → fingerprint (`Scripts/ScriptFingerprint`: code hash, namespaces hash, references hash incl. file lastWrite/package version, kind, TFM, opt level, UseAspNet, data connection) → in-memory compile cache → `ScriptCompiler.ParseAndCompileAsync(code, script)` → permutations → `CodeParser.Parse` (`ClientServerCSharpCodeParser` injects Config.Namespaces + ASP.NET usings; SQL wraps template) → `CodeParsingResult.GetFullProgram().ToCodeString()` (UserProgram + AdditionalCodeProgram + BootstrapperProgram) → `CSharpCodeCompiler.CreateCompilation` (ConsoleApplication, OverflowChecks(true), AllowUnsafe(true), generators via CSharpGeneratorDriver, embedded PDBs).
- Preprocessor symbols: `Compilation/PreprocessorSymbols.cs` applied in `CodeAnalysisService.GetParseOptions`; OmniSharp `<DefineConstants>` via `AppOmniSharpServer.SetPreprocessorSymbolsAsync`.
- References: abstract `Reference` (`AssemblyFileReference`, `AssemblyImageReference`, `PackageReference`) with STJ discriminator; script serialization `ScriptConfigData.References?`.
- OmniSharp project: `AppOmniSharpServer` writes csproj + `User_Program.cs` etc.; reacts to `Script*UpdatedEvent`s.
- Snippets today: hard-coded TS at `editor/providers/snippets/csharp.ts` feeding Monaco completion providers.
- Entry execution: generated top-level statements Program; bootstrapper embedded resource partial class.

## Required behavior / acceptance criteria

### 1. #load
Directive support in C# scripts: `#load "path-or-pattern"` lines parsed before compilation.
- Sources: `.cs` files; `.netpad` scripts (load their code; merge needed namespaces/references from loaded config where appropriate; root script's kind/TFM/data connection stay authoritative).
- Paths: absolute; relative to current saved script's directory; unsaved+unresolvable ⇒ clear diagnostic naming the directive line. Wildcards incl. recursive `**`; deterministic ordering (sorted expansion); transitive loads (loaded files may contain `#load`); cycle detection with diagnostic.
- Effective code = concatenation respecting order; diagnostics map to real source file/line (emit as separate syntax trees or use line-mapping when splicing — choose per CSharpCompiler structure; separate trees preferred).
- Fingerprint inputs: resolved load path set + each file content hash (+ referenced configs' relevant parts). Any change ⇒ recompile.
- OmniSharp/editor: loaded sources added to project so F12/diagnostics/navigation work; opened via plan 05 generalized text viewer.

### 2. Loaded ToDump
Global/static ToDump transformers declared in loaded sources participate through plan 01's registry (registration hook executed at program start — e.g., bootstrapper invokes discovered registration or loaded code calls the registration API). Conflicts: deterministic (documented: later-loaded wins / single transformer chain order = load order). Tested.

### 3. Compile-time script references
New `Reference` subtype or sibling concept `ScriptReference(path)` persisted in config: script A uses public types/members of B without executing B's top-level statements. Implementation approach: compile B (or its reusable surface) to an in-memory/on-disk assembly used as metadata reference for A (reuse dependency-resolution + cache keyed on B's fingerprint), or source-level include that strips top-level statements — decide after inspecting generated Program model; prefer assembly-reference semantics. Changes to B invalidate A (fingerprint includes referenced script fingerprint hash). Cycle ⇒ diagnostic. Serialization compatible (discriminator). OmniSharp sees symbols (project-to-project reference or shared source artifact).

### 4. My Extensions
One canonical editable document (app data location, e.g. `{appData}/extensions/MyExtensions.cs`): auto-included in every C# script compile; supports extension methods/types; optional own namespaces/references/packages via header directives if clean (else none initially); participates in OmniSharp (added to every project view); compile errors shown clearly and disablement switch (settings toggle + safe-mode: exclude broken extensions after repeated failure, with notification); never duplicated into saved scripts; fingerprint input.

### 5. User snippets
Persistent user snippet store (JSON under app data): name, prefix/trigger, description, body (Monaco snippet syntax), required namespaces, assembly refs, NuGet package refs. UI editor within settings. Insertion: body inserted via existing completion provider path; idempotent addition of declared namespaces/references/packages into current script config (no duplicates; preserve rest of config; report conflicts). No cloud sync.

### 6. Source generators
Verify/extend existing `CSharpGeneratorDriver` usage in `CSharpCodeCompiler`: discover analyzer/generator assets from NuGet package references (use dependency resolver's package asset groups; standard .NET packaging conventions `analyzers/dotnet/cs`), run before final compile, include generated syntax trees, surface generator diagnostics normally, include generator assets in fingerprint (package id/version/asset paths), failures non-fatal to app (reported as compile errors), isolation review: keep generation in compile step (already outside GUI process? verify — compile happens app-side in ClientServer model; assess risk and document; move to isolated compile worker only if cheap).

### 7. #:package / #:project directives
Standard .NET file-based app directive syntax for supported SDKs:
- `#:package PackageId@version` → translated to PackageReference resolution (same pipeline as PackageReference), preserved version, diagnostics for invalid id/version, included in fingerprint + OmniSharp restore.
- `#:project ../path/to.csproj` → resolve relative to file, add project output as assembly reference(s), monitor changes for invalidation, TFM-compat check with clear errors.
Plain `.cs` files opened in NetPad can run using these (ties to plan 05 generic files gaining explicit run capability).

### 8. Context preprocessor symbols
Extend `PreprocessorSymbols` with execution-context symbols applied at parse options + OmniSharp constants: `NETPAD_GUI`, `NETPAD_CLI` (headless/external runs), LINQPad-compatible alias `CMD` in CLI/headless contexts. Plumbing: runner sets context (GUI vs headless known by runner type); no broader CLI work.

### 9. Query-wide checked arithmetic
Add per-script flag (e.g. `ScriptConfig.CheckedArithmetic` nullable bool persisted optionally; absent/null ⇒ true = current behavior since compiler already emits checked). Expose in script properties UI; map to `CSharpCompilationOptions.WithOverflowChecks(...)`; OmniSharp `<CheckForOverflowUnderflow>`; fingerprint field. Support LINQPad-style directive if docs confirm one and it is cheap.

### 10. Main1..Main9
Alternate entry points: detect static methods `Main1`..`Main9` (parameterless, sync or Task-returning) in user code; commands/shortcuts `Run entry point N` execute exactly that method once (runner passes `SpecificCodeToRun`-style override or bootstrapper dispatch call `Program.Main<N>()` — adapt to generated-Program model without breaking ordinary scripts; default Run unchanged). Configurable shortcuts via BuiltinShortcuts.

### 11. xUnit mode
Script-kind or mode toggle enabling xUnit: ensure xunit packages available (auto-add framework refs/packages for the run, not persisted unless user opts in), discover `[Fact]`/`[Theory]` tests via xUnit's runner APIs (`xunit.runner.utility` v3 or v2 consistent with packages already resolvable), run all/selected with async support, report pass/fail/skip + duration + assertion failure message/stack into results (structured dump), cancellation, isolation so a crashing test doesn't kill app (separate run/host process reuse; guard with host restart on failure like existing infra). Not a test IDE: no test explorer tree beyond simple list/actions.

## Exact files/symbols

- Core runtime: `Compilation/Directives/*` new (loader/parser for #load/#:package/#:project), `ClientServerCSharpCodeParser.cs` (directive extraction before namespace injection), `ScriptCompiler.cs` (multi-tree composition, diagnostics mapping), `CSharpCodeCompiler.cs` (trees, overflow option param), `PreprocessorSymbols.cs`, `Scripts/ScriptConfig.cs` (+ serializer `ScriptConfigData`), `Scripts/ScriptFingerprint.cs`, references hierarchy (ScriptReference), dependency resolver interfaces/implementations (script-ref compile, package analyzer assets), My Extensions service (new, app-side + common), `ExecutionModel/ClientServer/*` (entry-point dispatch, xUnit mode orchestration), `External/*` parity where feasible.
- App: `AppOmniSharpServer.cs` (csproj updates: additional compile items, CheckForOverflowUnderflow, DefineConstants, project refs, package analyzers), settings additions, snippet store/UI, script properties UI fields.
- Tests: directive loader unit tests (relative/absolute/wildcard/**/transitive/cycle/order), fingerprint invalidation tests, parser/compiler integration tests (loaded diagnostics mapping, script ref cycles, generator source present, symbols presence per context, checked arithmetic IL behavior via small compilations, entry point selection), xUnit discovery/report tests with sample assemblies/snippets.

## Dependencies

Plan 01 (ToDump registry seam). Plan 05 consumes loaded-source opening + generic text viewers; not a hard dep for core compile work.

## Compatibility/persistence semantics

- `#load`/directives only activate when present in code; old scripts untouched.
- New config members optional/default-absent; old `.netpad` loads fine (ScriptConfigData tolerant).
- My Extensions absence = no-op; broken extension handling must not brick startup.
- CheckedArithmetic absent ⇒ true (current compiler behavior).
- Fingerprint record gains additive members; old cached entries simply miss (no migration needed).

## Constraints / non-goals

No debugger; no VB/F#; no general plugin platform; no export-to-file-based-app feature; don't globally disable caching; don't duplicate loaded source into saved scripts.

## Focused regression tests

See per-section items above; prioritize: directive resolution matrix; diagnostic file mapping; fingerprint sensitivity (each new input flips hash); script-reference invalidation chain A→B; My Extensions inclusion/exclusion; snippet insertion idempotency; generator end-to-end with a trivial test generator assembly; symbol matrix (GUI vs CLI vs CMD alias); arithmetic overflow behavior both modes; Main1..Main9 dispatch single-execution; xUnit Fact/Theory/async/failure reporting.

## Completion checklist

- [~] Section 1: `.cs`/`.netpad` loading, relative/absolute/glob/transitive expansion, cycle/error diagnostics, source mapping, loaded-content fingerprinting, loaded `.netpad` namespace/reference merging, resolver integration, and OmniSharp project integration are implemented. F12 opens trusted loaded paths through the root-restricted generic file viewer; paths outside configured roots remain in the read-only Code pane. Plain diagnostics are source-mapped, but a general diagnostic-to-viewer navigation command is not wired.
- [x] Section 2: loaded `ToDump` registration through Plan 01's registry, deterministic load order, generated module initializer, and run-lifecycle cleanup.
- [ ] Section 3: compile-time `ScriptReference` and dependency/cycle invalidation are not implemented.
- [ ] Section 4: My Extensions document, settings, compile integration, and OmniSharp integration are not implemented.
- [ ] Section 5: persistent user snippet store and namespace/assembly/NuGet reference insertion are not implemented.
- [~] Section 6: package analyzer assets are discovered and passed explicitly to generator compilation; their paths/mtime participate in the ClientServer fingerprint; generator diagnostics and load/run failures become compilation errors. Generator execution is still in-process with app-side compilation and is not isolated.
- [ ] Section 7: `#:package`/`#:project` directives and plain `.cs` execution are not implemented.
- [x] Section 8: GUI/headless context symbols are applied to compilation and OmniSharp project constants.
- [x] Section 9: checked arithmetic is configurable, persisted with a checked default, fingerprinted, and applied to compilation/OmniSharp.
- [x] Section 10: `Main1`..`Main9` parameterless sync/`Task` entry points, selection via `RunOptions.EntryPoint`, fingerprint/deployment-cache participation, and configurable Ctrl+Alt+Digit1–9 shortcuts.
- [ ] Section 11: built-in xUnit query/test mode is not implemented.
- [~] Fingerprint and OmniSharp invalidation cover loaded sources, loaded `.netpad` references, checked arithmetic, entry points, and generator assets; inputs for unimplemented references/extensions/directives/snippets are consequently not covered.
- [x] Focused tests added and passing. Broad validation is limited by missing .NET 7 reference/implementation assemblies, detailed below.
- [x] Follow-up implementation commit recorded below; master checklist reconciled centrally.

## Progress log

Initial implementation notes (2026-10-17; superseded where noted below):

- Added `LoadDirectiveProcessor` to expand relative/absolute `.cs` and `.netpad` loads, `*`/`?`/recursive `**` patterns, transitive loads, and deterministic sorted matches. It detects cycles and unresolved directives, emits `#error` diagnostics under `#line` mappings, merges loaded using directives and `.netpad` namespaces, and hashes loaded file paths/content into `ScriptFingerprint`. The initial pass did not merge `.netpad` references or update OmniSharp; both are now implemented.
- Loaded source navigation was initially absent. OmniSharp now receives expanded code, loaded `.netpad` references, and update/removal handling. The source-definition response maps loaded code back through `#line`; trusted files in configured library roots open in Plan 05's generic viewer at the mapped line. The root-restricted file service is intentional; outside-root paths remain viewable in the read-only Code pane.
- Loaded `ToDump` hooks are discovered and registered in load order through Plan 01's existing registry before user code runs; ScriptHost clears registrations after a run. No parallel transformer registry was added.
- NuGet `analyzers/dotnet/cs` assets flow through dependency resolution to `CSharpCodeCompiler`; they are excluded from metadata references. Generator errors become compilation diagnostics. ClientServer fingerprints include analyzer paths/mtime. Generator code still executes app-side and is not process-isolated.
- Checked arithmetic now defaults to checked, is persisted additively, exposed in script properties, included in fingerprints, and applied in normal, external, and in-memory compiler inputs and the OmniSharp project.
- Added `NETPAD_GUI` for GUI compilation and `NETPAD_CLI` plus `CMD` for external/headless compilation; OmniSharp uses the GUI and target-framework symbol set.
- Historical initial validation passed focused runtime checks (36 tests) and frontend Jest (32 suites/447 tests); later runs supersede its known environment failures.

- `5a972af6 Add script composition compiler features`

2026-10-18 — Integration follow-up:

- Added loaded `.netpad` references to both GUI and external dependency resolution and to OmniSharp. Loaded code now hoists usings without line-count changes, converts loaded file-scoped namespaces, and orders top-level statements ahead of types while retaining source mapping.
- Added loaded-source `ToDump` module initialization/cleanup, explicit analyzer assets + ClientServer fingerprinting, `Main1`–`Main9` runner selection, and per-entry-point configurable shortcuts. `RunOptions.EntryPoint` participates in the normal and external deployment cache identity.
- Added Plan 05 generic-viewer navigation for loaded sources when they are within configured library roots. Access outside those roots stays read-only in the Code pane; no arbitrary-path file access was added.
- Outstanding selected work: `ScriptReference`, My Extensions, persistent snippets and their reference insertion, `#:package`, `#:project`, built-in xUnit mode, generic diagnostics-to-viewer navigation, and generator process isolation.
- Focused .NET validation: 33 load/resolver/fingerprint tests; 21 compiler tests (the .NET 7-dependent case excluded); 8 ScriptCompiler tests; combined runtime/compiler/runner checks 71 passed; explicit generator-discovery failure test passed. Full `scripts/agent-test.sh`: 924 passed, 5 skipped, 1 failed because .NET 7 reference/implementation assemblies are unavailable under `/usr/share/dotnet` (`Can_Compile_CSharp11_Features`).
- `scripts/agent-build.sh src/Plugins/NetPad.Plugins.OmniSharp/NetPad.Plugins.OmniSharp.csproj` passed (0 errors, 4 warnings); `scripts/agent-build.sh src/External/OmniSharp.NET/OmniSharp.NET.csproj` passed (0 errors, 0 warnings), fixing the prior `KeyValuePair.Deconstruct` build failure. ScriptHost build passed with 7 warnings.
- Full frontend Jest passed (49 suites/504 tests); frontend `npx tsc --noEmit --pretty false` passed. App tests passed 66/66.
- `6b63bf9c Complete script composition compiler integration` — compiler/runtime, reference resolution, generator assets, entry-point dispatch, shortcuts, and OmniSharp integration.
- `67c2d4cc Open loaded sources in the generic viewer` — source navigation through the root-restricted Plan 05 viewer, with read-only fallback outside configured roots.
