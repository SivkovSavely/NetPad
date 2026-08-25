# AGENTS.md

Repository-wide instructions for AI coding agents working on this personal NetPad fork.

https://chatgpt.com/c/6a896aa2-912c-83ed-985a-a39d155fa7a2

## Repository model

This is a personal fork of NetPad intended to stay close to upstream while carrying focused personal fixes/features.

Expected remotes:

* `origin` — personal fork
* `upstream` — official `tareqimbasher/NetPad`

Branch roles:

* `main` — pristine mirror of `upstream/main`
* `personal` — long-lived branch containing personal changes. Temporary `feat/*` or `fix/*` branches may be used for larger or risky work, but are not required.
* `feat/*`, `fix/*` — focused development branches

Never commit personal changes to `main`.
Never push to `upstream`.
Do not alter remotes, discard user work, reset, stash, or rewrite history unless explicitly instructed.

Before editing, inspect `git status --short --branch`. Do not modify `main` or overwrite existing user work.

## General workflow

Minimize total model/context cost, not merely tool-call count. Batch independent searches/reads when possible. Prefer one targeted search followed by a few relevant reads over serial exploration. Once enough evidence exists to implement safely, stop exploring and edit.

Use context compression/DCP at natural phase boundaries when accumulated context would otherwise be repeatedly carried forward. Do not avoid compression merely to reduce tool-call count.

Do not reread unchanged files or investigate optional context.

Before editing:

1. Inspect the nearest analogous implementation and relevant tests. Reuse its runtime conventions (DI tokens/decorators, serialization, binding, registration, error handling, etc.), not just its types/API shape.
2. Trace behavior across layers when necessary.
3. Check whether current code already implements all or part of the requested behavior.
4. For bugs, infer the likely cause from the provided evidence and relevant code. Do not reproduce or execute the application unless needed for requested validation.

Before finishing, ensure every explicit requested behavior/constraint is represented by the implementation or a relevant regression test. Do not broaden this into a general speculative review.

Do not read `README.md`, `CONTRIBUTING.md`, or documentation trees by default.
Read documentation only when it is directly relevant to the current task.

Prefer:

1. searching for the exact relevant symbol/topic;
2. reading the smallest relevant file/section;
3. inspecting implementation/tests directly when they are authoritative.

Do not preload broad documentation for context.

Consult `CONTRIBUTING.md` only when preparing an upstream PR or when task-specific build/package instructions are needed.

Do not edit based only on filenames or assumptions.

Prefer:

* small focused changes;
* existing abstractions and conventions;
* minimal divergence from upstream;
* additive behavior over breaking behavior;
* focused regression tests for personal behavior when appropriate.

Avoid:

* unrelated refactors or cleanup;
* speculative abstractions;
* formatting churn;
* unrelated dependency upgrades;
* broad rewrites when a focused fix is sufficient.

## Upstream conventions

If preparing a contribution for upstream NetPad:

* use `feat/` for features and `fix/` for fixes;
* use imperative commit messages;
* include tests and follow repository coding standards;
* PRs should briefly describe the change and reference the issue when applicable;
* user docs live in `docs/wiki/`; technical docs in `docs/technical-docs/`;
* before an upstream PR, tell the user to run `just check-all`; do not run it yourself.

## Architecture

Important areas:

```text
src/Core/NetPad.Runtime              Core runtime, compilation, execution, Dump/presentation
src/Apps/NetPad.Apps.App             ASP.NET host
src/Apps/NetPad.Apps.App/App         Aurelia/TypeScript frontend
src/Apps/NetPad.Apps.Common          Shared app services/infrastructure
src/Apps/NetPad.Apps.Cli             npad CLI
src/Apps/NetPad.Apps.ScriptHost      Isolated script process
src/Apps/NetPad.Apps.Shells.*        Web/Electron/Tauri shells
src/Plugins/NetPad.Plugins.OmniSharp Code intelligence
src/Tests                            Tests
docs/technical-docs                  Architecture documentation
```

NetPad crosses process and language boundaries. A visible UI/runtime failure may originate elsewhere; trace the actual flow before patching.

## Validation policy

After implementation, perform relevant validation by default using the compact agent wrappers.

Available wrappers:

```text
scripts/agent-build.sh [dotnet build args...]
scripts/agent-test.sh [dotnet test args...]
scripts/agent-jest.sh [jest args...]
```

They preserve the underlying command exit code while suppressing routine successful output and retaining actionable diagnostics.

For .NET changes:

1. Prefer the narrowest relevant project/test/filter first when practical.
2. If focused validation passes, run `scripts/agent-test.sh` without arguments to validate the full solution.
3. `agent-test.sh` already builds the tested projects; do not also run `agent-build.sh` merely to repeat compilation.
4. Use `agent-build.sh` instead when compilation alone is the relevant check or running tests would add unnecessary work/output.

For frontend changes:

1. Prefer the narrowest relevant Jest suite first when practical.
2. If focused validation passes, run `scripts/agent-jest.sh` without arguments to validate the full frontend suite.

For changes spanning both .NET and frontend code, perform the relevant .NET and Jest validation.

When validation fails:

1. Diagnose failures attributable to the current task.
2. Fix them and rerun the narrowest affected check.
3. Once focused checks pass, rerun the appropriate broad validation once.
4. Do not investigate or modify unrelated pre-existing/environmental failures unless explicitly asked.
5. If broad validation fails only because of a clearly established unrelated failure, report it and stop rather than changing unrelated code.

Prefer the compact wrappers over raw `dotnet build`, `dotnet test`, or Jest. Use raw validation commands only when a wrapper cannot expose information needed to diagnose a failure; keep resulting output similarly focused.

Do not run extra validation merely for reassurance after the relevant broad checks have completed.

Do not launch the interactive NetPad application or perform manual UI validation unless explicitly requested.

Do not run unrelated linters, formatters, package operations, `cargo`, `just`, or other validation commands unless they are required by the task or explicitly requested.

## Personal behavior and upstream rebases

Preserve **behavioral intent**, not old source lines.

When upstream changes code touched by a personal patch:

1. Inspect the personal commit and its tests.
2. Inspect the new upstream implementation.
3. Determine whether upstream now implements the feature.
4. If not, adapt the personal behavior to the new architecture.
5. Preserve or update relevant regression tests.

Do not mechanically resolve conflicts with `ours`, `theirs`, or “accept both”.

Do not silently change personal:

* defaults;
* precedence;
* error behavior;
* side effects;
* supported inputs;
* edge cases.

If upstream appears to fully implement a personal patch, remove the redundant patch only after establishing semantic equivalence from code/tests.

If a conflict requires an ambiguous behavioral decision, explain it instead of guessing.

Useful commands:

```bash
git status
git diff
git show <commit>
git range-diff
```

## Commit discipline

Keep logically independent personal features in separate commits when practical.

Good:

```text
Fix .NET 10 SDK resolution
Add Util.RunCommand
Add JSON Dump formatting
```

Bad:

```text
My changes
```

For non-trivial behavior, commit messages should explain why the behavior exists. This helps future conflict resolution.

## Compatibility and special areas

Assume existing behavior is intentional unless evidence shows otherwise.

Take extra care when changing:

* `Dump`, `DumpOptions`, `Util`, or other script-facing APIs;
* script serialization/persisted settings;
* SDK discovery, MSBuild, compilation, ScriptHost, or OmniSharp;
* output/IPC/headless/MCP paths;
* filesystem/native-shell behavior;
* authentication, secrets, process execution, or paths.

For values crossing UI/API/serialization/persistence boundaries, preserve the existing representation and semantics of unset/default/null values.

For framework/SDK fixes, preserve coverage for both the failing framework and a known-working one when practical.

For Dump/output changes, consider GUI and headless/external execution paths where relevant.

Do not weaken security or expose credentials/tokens/secrets.

## Dependencies and formatting

Do not add dependencies unless existing platform/framework capabilities are insufficient and the maintenance cost is justified.

Respect `.editorconfig` and `.gitattributes`.

Avoid unrelated formatting or encoding churn.

Keep changes limited to files required by the task.

## Completion

Stop once all explicit requirements are implemented, appropriate tests are written, and relevant validation has completed. Do not continue exploring for hypothetical improvements.

Do not perform a separate broad self-review after validation unless there is evidence of a remaining problem.

Report concisely:

1. what changed;
2. files materially changed;
3. tests added/updated, if any;
4. validation commands run and their outcomes;
5. any remaining unrelated/environmental validation failure.

Do not claim broader validation than was actually performed.

## Output economy

Minimize narration. Prefer tool calls/edits over describing what you are about to do.
Do not repeat the task, explain obvious edits, or provide progress commentary unless needed.
Keep final reports terse. Preserve technical terms, code, commands, paths, and meaningful uncertainty exactly.

