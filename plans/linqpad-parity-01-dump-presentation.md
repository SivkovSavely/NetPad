# Plan 01 — Dump and presentation parity

Paste-ready work package. Read `plans/linqpad-parity-00-master.md` first for baseline architecture (section "Presentation pipeline") and rules.

## Task

Implement the remaining LINQPad-like presentation fundamentals around NetPad's existing `Dump`, `DumpContainer`, mutable output slots, `HtmlPresenter`, and serialization pipeline. Extend the recent mutable-output work; do not replace it.

## Current/relevant architecture

- `src/Core/NetPad.Runtime/Presentation/DumpExtension.cs` — static `Sink` (`IDumpSink`), `Dump()` extension overloads (value/Task/Span), all funnel into `Sink.ResultWrite(o, options[, outputId, isUpdate])`.
- `src/Core/NetPad.Runtime/Presentation/DumpOptions.cs` — record `(Title, CssClasses, CodeType, AppendNewLineToAllTextOutput, DestructAfterMs)` + internal `Order`.
- `src/Core/NetPad.Runtime/Presentation/DumpContainer.cs` — server-side slot: stable `Guid "N"` output id, `_isDumped` guard under lock, `Content`/`UpdateContent`/`Refresh`, internal `Dump()`. Writes via `Sink.ResultWrite(content, null, outputId, isUpdate)`.
- `src/Core/NetPad.Runtime/Presentation/Html/HtmlPresenter.cs` — static `HtmlSerializerOptions`; converters registered for fork extras (`HorizontalRunHtmlConverter`, `StyledValueHtmlConverter`, `HighlightedTextHtmlConverter`, `OnDemandValueHtmlConverter`); `SerializeToElement` wraps output in `div.group`, applies title/css/code/destruct; serializer depth/collection limits from `PresentationSettings.GetConfigFileValues()`.
- `src/Core/NetPad.Runtime/Presentation/UtilExtrasTypes.cs` + `Presentation/Html/UtilExtrasHtmlConverters.cs` — pattern to copy for any new presentational wrapper type: small POCO + O2Html `ElementConverter<T>`.
- `src/Core/NetPad.Runtime/ExecutionModel/ScriptServices/Util.Extras.cs` — `OnDemandRegistry` pattern (ConcurrentDictionary keyed by output id; `RegisterOnDemand` checks `ProgressBar.IsInteractiveSink()` seam; eager fallback when non-interactive; `ClearOnDemandRegistry()` called from ScriptHost `ScriptRunner.Run`).
- Tests conventions: `src/Tests/NetPad.Runtime.Tests/Presentation/*` (e.g. `DumpContainerTests.cs`, `UtilExtrasTests.cs`) install a fake sink via `DumpExtension.UseSink(...)`.

## Required behavior / acceptance criteria

### 1. ToDump customization
- Instance hook: parameterless method named `ToDump` (any visibility incl. private, declared on the type or inherited) whose return value replaces the object at dump time. Discovered automatically by the serialization pipeline; callers never invoke it.
- Script-global hook: `ToDumpGlobalTransformer` registry with API shape equivalent to LINQPad's script-wide `static object ToDump(object value)`; registered via a `Util`-level or presentation-level static (exact public surface chosen during implementation; must be settable from user code and from loaded sources per plan 04).
- Precedence: match LINQPad — resolve against current public docs during implementation; default assumption if docs are ambiguous: script-global transformer runs FIRST on the raw value, then instance `ToDump()` runs on the result. Document final decision in master plan decisions log.
- Never mutates the original value. Handles `null` (no hook invocation). Async dump paths pass through the same transformation.
- Cycle safety: if transformation result is reference-equal to input, or was already transformed in this traversal (visited-set per serialization traversal), stop transforming further.
- User code throwing inside hooks surfaces as an error-styled group for that node only ("Error in ToDump..."), stream continues.
- Applies inside nested members/collections too (i.e., implemented where O2Html walks children), not just root.

### 2. Richer DumpOptions
Extend the record additively (new optional params with defaults so existing call sites compile):
- MaxRows (`uint?`) / max collection length per dump
- MaxDepth (`uint?`)
- ForceExpand (`bool?`)
- Member include/exclude lists where compatible with the object renderer (`IncludeMembers`/`ExcludeMembers` `string[]?` or equivalent; implement via filtering in the custom serialization path)
- Per-type format strings where useful (`FormatStrings` or minimal equivalent)
- Expansion control (collapsed vs expanded default)
Add:
- `DumpOptions.Default` static concept + script-global defaults via `Util.DumpDefaults` (a `DumpOptions`-shaped mutable holder in `Util.Extras.cs`).
- Per-`DumpContainer` options property used when writing its slot.
Precedence (must hold): explicit per-call/per-container > `Util.DumpDefaults` > NetPad Results settings (`PresentationSettings` limits already applied at serializer level) > built-ins. Merge = explicit non-null member wins; unset (`null`) never overrides lower layer. Changing `DumpDefaults` never mutates app Settings.
Implementation note: per-dump depth/rows require threading options into serialization; simplest correct approach is a scoped ambient context (AsyncLocal or parameter pass-through) consumed by the ToDump-aware converter chain, restoring global behavior when absent.

### 3. Complete public DumpContainer surface
Add to existing class (keep OutputId mechanism):
- `Title`, `CssClasses` (applied to the group element of its slot)
- `Options` (`DumpOptions?`) merged with per-call precedence for updates
- `AppendContent(object?)` — appends inside same logical container (concatenates rendered content within the slot; implement by keeping a list of contents serialized together, or by appending HTML nodes host-side before send)
- `ClearContent()` — clears container content without corrupting following output order (slot remains; body becomes empty group)
- Safe invalidation: updating after app cleared results/rerun → update is dropped by fold buffer (already) and frontend ignores unknown ids (already); ensure no exceptions thrown from `Content` setter after disposal scenarios.
Acceptance: first Dump establishes slot; updates replace; append extends within slot; clear empties; ordering relative to ordinary output preserved (all writes go through normal ordered channel).

### 4. Output-composition helpers
New presentational wrapper types + `Util` factories (follow HorizontalRun/StyledValue pattern + HtmlConverter each):
- `HighlightIf(value, predicate)` — renders value with highlight css when predicate true (LINQPad-style conditional highlight)
- `WithCssClass(value, cssClass)` — adds CSS class(es) to rendered dump
- `WordRun(params items)` / `VerticalRun(params items)` — inline word-spaced run / vertical stack
- `WithHeading(heading, value)` — titled wrapper
All compose recursively with objects, nested runs, DumpContainer content, Hyperlinq (plan 02), Markdown/LaTeX viewers (plan 02/03): i.e., converters render child items through the standard serializer (`HtmlSerializer.Serialize(child, options)`) rather than flattening to strings.

### 5. Util.Dif
`Util.Dif(a, b)` returning a dumpable structured difference. Support meaningfully:
- strings: line-oriented diff (LCS-based; no external dep) with added/removed lines highlighted
- sequences: positional/indexed comparison producing additions/removals/changes entries
- dictionaries: key-wise added/removed/changed
- objects: member-wise structural comparison via reflection (public readable props/fields)
Render human-readable groups via normal result system (custom converter). Keep source-friendly signature(s), e.g. `Dif(object? expected, object? actual)` plus generic overloads if useful. No heavyweight diff framework.

### 6. DumpTell
`someExpr.DumpTell()` — extension `DumpTell<T>(this T? o, string? title = null, [CallerMemberName] string? caller = null, [CallerArgumentExpression(nameof(o))] string? expression = null)` producing title from source expression when no explicit title. Handle null, multiline expressions (collapse whitespace/newlines), extension-method usage naturally. Ordinary `Dump()` unchanged.

### 7. Async enumerable dumping
- `DumpAsync<T>(this IAsyncEnumerable<T> source, ...)` (+ `Util.DumpAsync` mirror): enumerates asynchronously, streams items into ONE result block (single output slot updated in place via OutputId when interactive; buffered otherwise), respects row limit from options/settings, cancellation token support, enumeration exception rendered as error group after partial rows.
- Do not emit one unrelated block per item.

### 8. Exception dump improvements
Define metadata/hooks only in this plan: ensure dumped exceptions carry file/line info usable for links (they already do via stack trace text); add a frontend-side linkification hook point in `dump-container.ts` post-processing (`postProcessRenderedElements`) that recognizes `File:line` patterns and renders navigable anchors calling an injectable navigation callback (default no-op; plan 07 wires real navigation/decompile). No debugger work.

## Exact files/symbols

Likely touched/created:
- Modify: `Presentation/DumpOptions.cs`, `Presentation/DumpExtension.cs`, `Presentation/DumpContainer.cs`, `Presentation/Html/HtmlPresenter.cs`, `Presentation/Html/UtilExtrasHtmlConverters.cs` (or new sibling converter file), `Presentation/UtilExtrasTypes.cs` (new wrapper types), `ExecutionModel/ScriptServices/Util.Extras.cs` (DumpDefaults, Dif, composition factories, DumpAsync), maybe new partial `Util.Presentation.cs`.
- New: `Presentation/ToDump.cs` (hook discovery + global registry + traversal context), possibly `Presentation/Diff/DifEngine.cs` + diff types.
- Frontend: `output-pane/components/dump-container.ts` (exception linkification hook), minor scss if needed.
- Tests: `src/Tests/NetPad.Runtime.Tests/Presentation/ToDumpTests.cs`, `DumpOptionsTests.cs`, extend `DumpContainerTests.cs`, `UtilExtrasTests.cs` (composition helpers, Dif, DumpTell), async dumping tests; frontend jest spec if linkification logic is extracted testably.

## Dependencies

None on other plans. Provides the global-transformer seam that plan 04 loaded-source `ToDump` uses, and the option plumbing plans 02/03 build on.

## Compatibility/persistence semantics

- All additive; existing scripts compile/run unchanged. `DumpOptions` record gains optional params only (positional construction still valid).
- No persisted settings changes required. Serializer-level defaults continue coming from Results settings.
- Headless/external paths get identical semantics because everything funnels through `IDumpSink`/writers (fold buffer folds slot updates).

## Constraints / non-goals

- No DataGrid; no EChart; no debugger.
- No second object serializer — extend O2Html converter chain.
- Don't change ordinary `Dump()` output shape.
- Don't make `DumpDefaults` write to Settings.
- Avoid parser hacks in DumpTell beyond CallerArgumentExpression.

## Focused regression tests

1. ToDump: public instance hook; private instance hook; inherited hook; global hook; precedence order; null input untouched; self-returning hook no infinite loop; cyclic A→B→A transform terminates; nested members/collections transformed once; hook throws → error group, subsequent dumps fine; no-hook fallback identical to today.
2. DumpOptions: precedence matrix (explicit > defaults > settings > builtin); null members don't override; row/depth limits applied; DumpContainer.Options honored; DumpDefaults doesn't touch Settings.
3. DumpContainer: establish/update/append/clear ordering with interleaved ordinary dumps; two containers independent ids; clear then update safe; headless fold buffer produces one folded entry.
4. Composition helpers recursive composition (run-in-run, run containing styled value + container).
5. Dif: strings (added/removed lines), sequences (insert/remove/modify), dictionaries, objects, mixed/null.
6. DumpTell: title equals collapsed expression; explicit override wins.
7. DumpAsync: single block; row limit; cancellation stops cleanly; exception mid-enumeration yields error suffix.

## Completion checklist

- [ ] All acceptance criteria above implemented
- [ ] Tests listed added and passing locally (`scripts/agent-test.sh --filter <...>` narrow first)
- [ ] Master plan checklist ticked + commit hashes logged below
- [ ] Concise user validation notes left here

## Status: COMPLETE (runtime + frontend; see notes)

## Progress log

- `8d607973` — ToDump (instance + script-global transformers), richer DumpOptions + `Util.DumpDefaults` (`DumpOptions.Default`), complete DumpContainer surface, composition helpers (HighlightIf/WithCssClass/WordRun/VerticalRun/WithHeading). Vendored O2Html converter-cache fix. Tests: ToDump/DumpOptions/DumpContainerSurface/CompositionHelper suites.
- `783e3a8d` — Dumped-error source-location linkification seam (`source-linkify.ts` + `onNavigateToSource` wired to session open) + Jest spec.
- Util.Dif implemented in the same runtime commit (DiffEngine + DiffResultHtmlConverter + `Util.Dif`).
- DumpTell and DumpAsync(IAsyncEnumerable) implemented in the same runtime commit.

## Implementation notes / decisions made during execution

1. **ToDump precedence**: script-global transformer runs FIRST on the raw value, then instance hooks chain on successive results (guarded, once per traversal by reference). Cyclic/self-returning chains are bounded (reference set + 16-step guard) and render raw instead of looping.
2. **Mechanism**: a `ToDumpHtmlConverter` placed first in NetPad's O2Html converter list intercepts values when a global transformer is registered or the type declares an instance hook; transformed results re-enter the standard pipeline through a converter-less fallback serializer using the *result's runtime type*.
3. **O2Html vendored change**: `HtmlSerializer.GetConverter` only uses its static per-type cache when no custom converters are registered (custom sets differ per instance and would poison the shared cache). Plus `InternalsVisibleTo("NetPad.Runtime")`.
4. **Per-dump options plumbing**: active `DumpOptions` flow via a ThreadStatic ambient context pushed in `SerializeToElement`; gated converters (member filter incl. table headers, row limiting, format strings) activate only when relevant options exist, so default rendering is byte-identical to before.
5. **Expanded** renders as a `data-expanded` attribute (no frontend collapse behavior exists yet to honor it); FormatStrings apply to IFormattable/scalars by full or simple type name.
6. **Script-global defaults merge point**: user-facing `Dump()` entry points and DumpContainer writes; direct internal `HtmlPresenter` calls (system notices, SQL) intentionally bypass defaults so scripts cannot restyle host messages.
7. **DumpAsync**: interactive sessions get one slot ("AE" id) updated every 50 items; row cap stops consuming the source; cancellation appends a metatext notice; enumeration errors keep partial rows, append an error group, and rethrow.
8. **Exception links**: anchors carry data attributes; navigation callback currently opens via `session.openByPath` best-effort (line positioning deferred to plan 05/07).

## Validation performed (by agent)

- `scripts/agent-test.sh src/Tests/NetPad.Runtime.Tests/... --filter Presentation` → 77/77 pass.
- Full solution `scripts/agent-test.sh` → all green except two pre-existing/environmental failures unrelated to this work:
  - `OmniSharp.NET` vendored project fails to compile on this machine even at baseline (verified via stash).
  - `Can_Compile_CSharp11_Features` requires .NET 7 SDK reference assemblies not installed here.
- Frontend: new `source-linkify.spec.ts` → 6/6 pass; `tsc --noEmit` clean.

## User validation suggestions

Run a script like:

```csharp
// ToDump
class P { public string Name = "x"; object ToDump() => new { Name, Custom = true }; }
new P().Dump();
using (Util.RegisterToDumpTransformer(v => v is int i ? i * 10 : v))
    42.Dump();

// Defaults + options
Util.DumpDefaults = new DumpOptions(MaxRows: 3);
Enumerable.Range(1, 100).ToArray().Dump();                       // truncated to 3
Enumerable.Range(1, 100).ToArray().Dump(new DumpOptions(MaxRows: 5)); // explicit wins
Util.DumpDefaults = new DumpOptions();

// Container
var dc = new DumpContainer("loading") { Title = "progress" }.Dump();
dc.AppendContent(new { Step = 1 });
dc.ClearContent();
dc.UpdateContent("done");

// Composition / Dif / Tell
Util.WithHeading("Hi", Util.WordRun("a", "b")).Dump();
Util.Dif("line1\nline2", "line1\nchanged").Dump();

// Async
await GenerateAsync().DumpAsync("stream");
```

Then trigger an exception with a file path in the message and confirm the `file.cs:12` text is clickable.
