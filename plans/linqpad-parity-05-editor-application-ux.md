# Plan 05 — Editor and application UX parity

Paste-ready work package. Read master plan (App/frontend section) first. Mostly independent; uses plan 02's Markdown renderer for previews.

## Task

Implement selected High/Medium application features plus revert/bookmarks: global feature search, searchable settings, document switcher, MRU UX, multiple script-library roots, full-text search, arbitrary text files, Markdown/HTML live preview, revert to disk, per-script CPU/memory monitor, bookmarks incl. numbered.

## Current/relevant architecture (verified)

- Viewers: `work-area/work-area-service.ts`, `viewers/viewer-registry.ts` (`canHandle(viewable)` first-match), `viewer-host.ts` (per-host viewer instances map), `viewable-object.ts` base with capability pairs, `viewable-script-document.ts` owning `TextDocument`.
- Tab order: `tab-bar/tab-bar.ts` persists ordered ids to localStorage `tab-bar.${host.name}.viewables-order`.
- Editor: `core/@application/editor/text-document.ts` (`textModel` lazy, `setText(setter,...)`), `text-editor.ts` (`viewStates: Map<docId, ICodeEditorViewState>` in-memory only), transient `ITextEditor`.
- Session restore backend-driven (`Sessions/Session.cs` + `AppSetupAndCleanupBackgroundService`).
- Recents: `RecentScriptsService` + `/session/recent*` endpoints + `recent-scripts-store.ts`; consumed by `BuiltinActionProvider.goToScript` quick pick.
- Shortcuts/actions: `shortcuts/builtin-shortcuts.ts` (`ShortcutIds`, `.withCtrlKey().withKey(...).captureDefaultKeyCombo().configurable()`), `shortcut-manager.ts`, settings page `keyboard-shortcut-settings`.
- Settings: `Configuration/Settings.cs` (+ option classes), REST GET/PUT, `SettingsUpdatedEvent`; frontend mirror generated into `api.ts`.
- Library root: `Settings.ScriptsDirectoryPath`; `Apps/NetPad.Apps.Common/Scripts/FileSystemScriptRepository.cs` enumerates recursively.
- Monaco available in app; highlight.js present.

## Required behavior / acceptance criteria

### 1. Global feature search
Command-palette-style app-wide search over registered actions/commands/menu items/settings entries (metadata-driven; reuse action providers, menu service definitions, and a new lightweight settings-metadata source — no second hand-maintained registry). Fuzzy match on name+aliases; keyboard-first modal (Monaco QuickInput-style or custom overlay); executing runs command or navigates to the setting's location. Configurable shortcut (e.g. Ctrl+Shift+P style id `shortcut.featuresearch.open`) avoiding collisions.

### 2. Searchable Settings
Filter box on settings window matching title/description/keywords/category; result selection scrolls/focuses the actual control; definitions not duplicated (derive from existing view models).

### 3. Open-document switcher
Fast searchable list of open documents across viewer hosts (scripts + generic files): fuzzy name/path, dirty indicator, keyboard nav, Enter activates correct host/tab; MRU weighting; configurable shortcut (Ctrl+Tab-ish id).

### 4. Recent Files / MRU UX
Elevate RecentScriptsStore into menu/action surfaces: searchable recent list, remove-missing cleanup (existing prune logic preserved/extended), open via normal session/viewer path; include other supported file docs once plan section 7 lands.

### 5. Multiple script-library roots
`Settings.ScriptLibraryRoots: List<string>` additive (primary remains `ScriptsDirectoryPath`): explorer groups by root; create-new targets explicit root chooser defaulting primary; canonicalization dedupes; removing a root never deletes files; nested/overlapping roots handled intentionally (document rule: most-specific root wins for grouping); lookup/search/GetMyScripts cover all roots; single-root users unaffected. Generalize `FileSystemScriptRepository` (multi-root enumeration) rather than parallel repositories.

### 6. Full-text search across roots
Global search panel/query service over configured roots: query string; grouped results file→line matches w/ preview; click navigates (open doc at line); supports `.netpad`/`.cs`/text files; binary skip; async non-blocking with cancellation/restart; large-tree sanity (cap results, incremental scan); case toggle optional, literal fast path mandatory. Server-side C# implementation endpoint (no ripgrep dependency).

### 7. Arbitrary text files/extensions
New `ViewableFileDocument extends ViewableTextDocument` + viewer registration handling text files by extension (txt/cs/json/xml/md/html/etc. → Monaco language mapping): open/edit/save/Save As (where shell supports), dirty tracking independent of Script.IsDirty, external path identity, close confirmation, session restoration, recents integration, bookmarks/revert support hooks. Must NOT make arbitrary files executable as scripts; explicit run affordance only where composition plan enables it (.cs later).

### 8. Markdown live preview
For `.md` documents: live preview pane/split using plan 02 renderer; updates debounced on edit; preserves editor state/view state; offline.

### 9. HTML live preview
For `.html/.htm`: sandboxed iframe preview updating on edit (debounced); no privileged app API exposure (sandbox attrs; separate origin/frame restrictions as feasible); full-document HTML supported.

### 10. Revert File to Disk
"Revert File" action for saved file-backed docs/scripts: re-read disk; if buffer differs confirm destructive change per app conventions; replace buffer + mark clean + refresh script config model when applicable; handle deleted/unreadable gracefully; other tabs untouched. Configurable shortcut.

### 11. Per-script CPU/memory monitor
While an environment's process runs: poll low-frequency (e.g. 2s) Process CPU% + working set (script-host PID from environment/runner); status-bar/toolbar display unobtrusive; polling stops when idle/closed; zero/stale handles fine; KeepRunning scripts remain measurable. Cross-platform via `System.Diagnostics.Process` (note macOS CPU% caveats).

### 12. Bookmarks + numbered bookmarks
Monaco decorations-based: toggle bookmark at line; next/previous; clear all; numbered 0–9 set/toggle+jump. Gutter glyphs; stickiness via decoration range tracking across edits; commands configurable; per-document storage: saved-file docs keyed by canonical path persisted in workspace state (plan 06 consumes) with in-memory fallback now; unsaved scripts keyed by script id; restored with workspace/session state (plan 06 wires persistence fully).

## Exact files/symbols

- Frontend: `main-menu/`, new `feature-search/` + `document-switcher/` components under windows/main; `windows/settings/settings.*` search additions; `work-area/viewers/file-viewer/` (new ViewableFileDocument + viewer); `tab-bar` unchanged; editor bookmark manager under `editor/bookmarks.ts` + decoration wiring in `text-editor.ts`; output of search service client in `core/@application`.
- Backend: `FileSystemScriptRepository` multi-root; new `Controllers/SearchController` (full-text), `Services/FileSearchService`; settings additions (`ScriptLibraryRoots`, maybe monitor options); resource-monitor endpoint(s) on environments controller or SignalR event; recents generalization if needed.
- Tests: repository multi-root enumeration; file search service (grouping/binary skip/cancellation); snippet-free frontend logic extracted pure where testable (fuzzy matcher, MRU ordering); C# settings upgrade path tests; bookmark model unit tests (line mapping after simulated edits via monaco model in jest if harness permits, else pure index math).

## Dependencies

Plan 02 markdown renderer (previews). Plan 06 consumes: generic docs, bookmarks persistence, undo journal, tab order migration.

## Compatibility/persistence semantics

- New settings members optional; absent ⇒ current single-root behavior.
- localStorage tab order remains until plan 06 migrates it.
- Bookmarks stored outside `.netpad` files (workspace-only).
- No changes to script save format.

## Constraints / non-goals

No debugger/AI; don't force ripgrep dependency; don't duplicate settings definitions; avoid hard-coded key listeners (use shortcut system); don't break existing single-root flows.

## Focused regression tests

- Feature-search registry derivation (actions+settings metadata union; alias/fuzzy scoring).
- Switcher activation across two hosts incl. generic docs.
- Multi-root repo: grouping/dedupe/removal safety/nested roots.
- File search: literal match, preview line, cancellation mid-scan, binary skip, .netpad code extraction (search code section not metadata JSON).
- Generic doc lifecycle: dirty/save/save-as/close-confirm/session-restore serialization round-trip.
- Revert: clean vs dirty paths; deleted-file error.
- Monitor: mocked process stats update loop start/stop.
- Bookmarks: toggle/navigate/numbered jump; position tracking after insert/delete lines above bookmark (pure-model test).

## Completion checklist

- [ ] Sections 1–12 implemented with criteria
- [ ] Shortcut registrations done through BuiltinShortcuts
- [ ] Tests added/passing
- [ ] Master checklist updated + commits logged below

## Progress log

(commits appended here as they land)
