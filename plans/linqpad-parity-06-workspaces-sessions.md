# Plan 06 — Workspaces, sessions, shelving, state restoration

Paste-ready work package. Read master plan (App/frontend + persistence rules) first. Depends on plan 05 (generic file docs, bookmarks) and benefits from 02/03 completion for full state fidelity.

## Task

Build on existing `ISession`/`Session` (backend), WorkArea/ViewerHosts, and unsaved-script restoration to deliver: restore-all-documents (saved/unsaved/generic), working-set semantics, undo/redo history persistence, named workspaces, shelving, crash-safe versioned persistence. Do not replace the session mechanism wholesale unless inspection proves it cannot support this.

## Current/relevant architecture (verified)

- Backend session: `src/Core/NetPad.Runtime/Sessions/Session.cs` persists `session.openScripts` (Guid[]) and `session.active` via `ITrivialDataStore` with 500 ms debounce; `AppSetupAndCleanupBackgroundService.StartingAsync` reopens auto-saved scripts then saved open scripts then activates best candidate; `StoppingAsync` closes all.
- Auto-save scripts: `IAutoSaveScriptRepository` (unsaved script recovery already works; files under `Settings.AutoSaveScriptsDirectoryPath`).
- Frontend tab order: localStorage per viewer-host name (`tab-bar.${host.name}.viewables-order`) — migrate into workspace state.
- Monaco view states: in-memory only (`TextEditor.viewStates`). Undo: Monaco model history only.
- `TextDocument.setText(setter,...)` distinguishes local vs server edits.
- Data store infra: `ITrivialDataStore` (file-backed JSON per key) — reuse for new versioned documents with atomic write wrapper.
- Generic docs + bookmarks land in plan 05.

## Required behavior / acceptance criteria

### 1. Restore all open documents
On startup restore: saved scripts; unsaved scripts (existing recovery preserved); generic files (plan 05); tab ordering; active document; active viewer host; host arrangement; cursor/selection; scroll/view state; dirty/clean flags. Never reopen docs the user explicitly closed before shutdown. Missing saved file ⇒ clear missing-file placeholder/dialog offering removal from workspace; startup never crashes on a bad entry.

### 2. Working set incl. modified files
Persist unmodified AND modified opens. For modified file-backed docs: preserve unsaved buffer inside workspace state alongside disk path; detect disk mtime/hash change while closed; on restore present explicit conflict choice (keep buffer / take disk / split copy); never silently overwrite either.

### 3. Persist undo/redo
Choose smallest supported mechanism after inspecting Monaco API surface available in-app (candidate: capture `model.undo()`/redo stacks unsupported ⇒ implement NetPad-level edit journal of inverse operations recorded from `onDidChangeModelContent` deltas, capped). Requirements: post-restart undo reaches pre-restart edits; redo where applicable; configurable/reasonable cap (e.g. last N ops or size budget); clean unchanged reopen creates no fake edits; Revert-to-disk resets chain intentionally after confirmation.

### 4. Named workspaces
Workspace document owns: open docs (+order per host), active doc/host, unsaved buffers, editor view states, bookmarks refs, undo journal, optional pane layout. Operations: create/rename/switch/delete + default workspace implicit for existing users. Switching persists current before opening next; never copies actual script files. Workspace switcher UI (command + menu).

### 5. Shelving
Shelf = recoverable snapshot of unsaved work (not VCS): explicit "Shelve current work"; automatic safe shelf on app update/exit when unsaved changes exist; list shelves; restore; delete. Captures: unsaved scripts, modified-file buffers, identities/paths, cheap editor state (cursor/selection). Restore conflicts against newer disk changes detected (mtime/hash) with explicit resolution. Simple versioned JSON representation.

### 6. Crash-safe persistence
Debounced writes during work; atomic temp-file+rename replacement; flush on orderly shutdown; keep previous valid copy (`.bak` rotation) — corrupt newest ⇒ recover previous gracefully; schema `version` field with tolerant loader (unknown fields ignored; missing ⇒ defaults). No database.

## Exact files/symbols

- New C#: `Sessions/Workspaces/*` (models: `WorkspaceState`, `DocumentState`, `BufferState`, `Shelf`; service `IWorkspaceService`/impl using `ITrivialDataStore`-like storage but atomic writer; endpoints controller `Controllers/WorkspacesController` + events), integration into `AppSetupAndCleanupBackgroundService` startup sequence (workspace load → env opening incl. generic docs → buffers applied → conflict resolution flow).
- Frontend: `core/@application/workspaces/` client; `work-area-service` save/restore hooks (serialize hosts/viewables/order); `text-editor.ts` view-state serialization (`ICodeEditorViewState.toJSON()/fromJSON()` are supported Monaco APIs — verify and use); bookmark persistence hookup from plan 05; undo journal in `editor/text-document.ts` or new `editor/edit-journal.ts`.
- Migration: read old localStorage tab-order keys once into default workspace then remove.
- Tests: state serialization round-trips (docs/order/active/buffers/views/bookmarks/journal caps); corrupt-file fallback; conflict detection matrix (disk changed vs buffer changed vs both vs neither); switch-workspace persistence ordering; migration from localStorage; journal replay producing identical text + bounded memory; shelf create/list/restore/delete + conflict case.

## Dependencies

Plan 05 (ViewableFileDocument, bookmarks, revert action semantics). Undo journal independent otherwise. Should land before/with heavy interactive use of plans 02/03 outputs (state fidelity), but no hard compile deps.

## Compatibility/persistence semantics

- Existing users: default workspace created transparently; behavior today (open scripts restored) unchanged apart from richer fidelity.
- All new state in dedicated versioned JSON docs; `.netpad` files untouched; transient UI state never written into scripts.
- Old session save-keys remain readable for one-version migration window (import then supersede).

## Constraints / non-goals

No Git integration; no database; no cloud sync; do not persist pane layout beyond what naturally belongs (allowed optional); don't break auto-save-script recovery.

## Focused regression tests

Listed per-section above; add crash-safety test simulating truncated newest file.

## Completion checklist

- [ ] Sections 1–6 implemented with criteria
- [ ] Migration + fallback paths tested
- [ ] Master checklist updated + commits logged below

## Progress log

(commits appended here as they land)
