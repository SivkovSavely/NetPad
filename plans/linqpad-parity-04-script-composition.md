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

- [ ] Sections 1–11 implemented with criteria
- [ ] Fingerprint/OmniSharp invalidation covered everywhere inputs changed
- [ ] Tests added/passing
- [ ] Master checklist updated + commits logged below

## Progress log

(commits appended here as they land)
