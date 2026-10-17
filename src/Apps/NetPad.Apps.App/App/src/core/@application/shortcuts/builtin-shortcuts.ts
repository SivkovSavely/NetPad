import {KeyCode} from "@common";
import {CreateScriptDto, RunOptions, Settings} from "@application/api";
import {Shortcut} from "./shortcut";
import type {ITextEditor} from "../editor/text-editor";
import {ITextEditorService} from "../editor/itext-editor-service";
import {INativeDialogService} from "@application/dialogs/inative-dialog-service";
import {WindowParams} from "@application/windows/window-params";
import {ShellType} from "@application/windows/shell-type";
import {ISession} from "@application/sessions/isession";
import {IScriptService} from "@application/scripts/iscript-service";
import {ISettingsService} from "@application/configuration/isettings-service";
import {IWindowService} from "@application/windows/iwindow-service";
import {IWorkAreaService} from "../../../windows/main/work-area/work-area-service";
import {INativeTextFileService} from "@application/dialogs/inative-text-file-service";
import {FeatureSearchService} from "@application/feature-search/feature-search";
import {OpenViewableQuickInput} from "@application/editor/open-viewable-quick-input";
import {getBookmarkManager} from "@application/editor/bookmark-manager";
import {FileSearchQuickInputService} from "@application/file-search/file-search-quick-input";

export enum ShortcutIds {
    openCommandPalette = "shortcut.commandpalette.open",
    quickOpenDocument = "shortcut.documents.quickopen",
    openLastActiveDocument = "shortcut.documents.switchtolastactive",
    newDocument = "shortcut.documents.new",
    openFile = "shortcut.documents.open",
    closeDocument = "shortcut.documents.close",
    saveDocument = "shortcut.documents.save",
    saveAllDocuments = "shortcut.documents.saveall",
    runDocument = "shortcut.documents.run",
    cancelAndExecute = "shortcut.documents.cancelandexecute",
    openDocumentProperties = "shortcut.documents.properties",
    openSettings = "shortcut.settings.open",
    openOutput = "shortcut.output.open",
    openExplorer = "shortcut.explorer.open",
    openNamespaces = "shortcut.namespaces.open",
    reloadWindow = "shortcut.window.reload",
    zoomIn = "shortcut.window.zoomIn",
    zoomOut = "shortcut.window.zoomOut",
    zoomReset = "shortcut.window.zoomReset",
    vimModeToggle = "shortcut.editor.vim.toggle",
    featureSearchOpen = "shortcut.featuresearch.open",
    openDocumentSwitcher = "shortcut.documents.switcher",
    openTextFile = "shortcut.documents.openTextFile",
    revertDocument = "shortcut.documents.revert",
    toggleBookmark = "shortcut.editor.bookmark.toggle",
    nextBookmark = "shortcut.editor.bookmark.next",
    previousBookmark = "shortcut.editor.bookmark.previous",
    clearBookmarks = "shortcut.editor.bookmark.clear",
    searchFiles = "shortcut.files.search",
}

const bookmarkNumberKeys: KeyCode[] = [
    KeyCode.Digit0, KeyCode.Digit1, KeyCode.Digit2, KeyCode.Digit3, KeyCode.Digit4,
    KeyCode.Digit5, KeyCode.Digit6, KeyCode.Digit7, KeyCode.Digit8, KeyCode.Digit9,
];

export const BuiltinShortcuts = [
    new Shortcut(ShortcutIds.openCommandPalette, "Command Palette")
        .withKey(KeyCode.F1)
        .hasAction(ctx => {
            const editor = ctx.container.get(ITextEditorService).active?.monaco;

            if (!editor) return;

            editor.focus();
            editor.trigger("", "editor.action.quickCommand", null);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.quickOpenDocument, "Go to Script")
        .withCtrlKey()
        .withKey(KeyCode.KeyT)
        .hasAction(ctx => {
            const editor = ctx.container.get(ITextEditorService).active?.monaco;

            if (!editor) return;

            editor.focus();
            editor.trigger("", "netpad.action.goToScript", null);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.featureSearchOpen, "Search Features")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.KeyP)
        .hasAction(async ctx => {
            const tabs = (await import("../../../windows/settings/window")).SettingsTabs;
            await ctx.container.get(FeatureSearchService).open(tabs);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openDocumentSwitcher, "Switch Open Documents")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.Tab)
        .hasAction(ctx => ctx.container.get(OpenViewableQuickInput).show())
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.searchFiles, "Search Files")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.KeyF)
        .hasAction(ctx => ctx.container.get(FileSearchQuickInputService).open())
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openLastActiveDocument, "Switch to Last Active Script")
        .withCtrlKey()
        .withKey(KeyCode.Tab)
        .hasAction((ctx) => ctx.session.activateLastActive())
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.newDocument, "New")
        .withCtrlKey()
        .withKey(KeyCode.KeyN)
        .hasAction((ctx) => ctx.container.get(IScriptService).create(new CreateScriptDto()))
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    ...(WindowParams.shell === ShellType.Browser ? [] : [
        new Shortcut(ShortcutIds.openFile, "Open File")
            .withCtrlKey()
            .withKey(KeyCode.KeyO)
            .hasAction(async (ctx) => {
                const dialogService = ctx.container.get(INativeDialogService);
                const paths = await dialogService.showFileSelectorDialog({
                    title: "Open Script",
                    filters: [{name: "NetPad Script", extensions: ["netpad"]}],
                    multiple: true,
                });

                if (!paths || paths.length === 0) return;

                const session = ctx.container.get(ISession);
                for (const path of paths) {
                    try {
                        await session.openByPath(path);
                    } catch (err) {
                        console.error("Failed to open file:", path, err);
                    }
                }
            })
            .captureDefaultKeyCombo()
            .configurable()
            .enabled(),

        new Shortcut(ShortcutIds.openTextFile, "Open Text File")
            .withCtrlKey()
            .withShiftKey()
            .withKey(KeyCode.KeyO)
            .hasAction(async ctx => {
                const files = await ctx.container.get(INativeTextFileService).openTextFiles({
                    title: "Open Text File",
                    filters: [
                        {name: "Text Files", extensions: ["txt", "text", "cs", "json", "xml", "md", "markdown", "html", "htm", "css", "js", "ts", "yaml", "yml", "ini", "sql"]},
                        {name: "All Files", extensions: ["*"]},
                    ],
                    multiple: true,
                });

                if (!files) return;
                const workArea = ctx.container.get(IWorkAreaService);
                for (const file of files) {
                    try {
                        await workArea.openFile(file);
                    } catch (error) {
                        console.error("Failed to open text file:", file.path, error);
                    }
                }
            })
            .captureDefaultKeyCombo()
            .configurable()
            .enabled(),
    ]),

    new Shortcut(ShortcutIds.closeDocument, "Close")
        .withCtrlKey()
        .withKey(KeyCode.KeyW)
        .hasAction((ctx) => {
            const workArea = ctx.container.get(IWorkAreaService);
            const host = workArea.viewerHosts.active;
            if (host?.activeViewable) void host.activeViewable.close(host);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.saveDocument, "Save")
        .withCtrlKey()
        .withKey(KeyCode.KeyS)
        .hasAction(async (ctx) => {
            const viewable = ctx.container.get(IWorkAreaService).activeViewable;
            if (viewable?.canSave()) await viewable.save();
        })
        .captureDefaultKeyCombo()
        .enabled(),

    new Shortcut(ShortcutIds.saveAllDocuments, "Save All")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.KeyS)
        .hasAction(async (ctx) => {
            const workArea = ctx.container.get(IWorkAreaService);
            const viewables = new Set(workArea.viewerHosts.items.flatMap(host => [...host.viewables]));
            for (const viewable of viewables) {
                if (viewable.isDirty && viewable.canSave()) await viewable.save();
            }
        })
        .captureDefaultKeyCombo()
        .enabled(),

    new Shortcut(ShortcutIds.revertDocument, "Revert File")
        .withCtrlKey()
        .withAltKey()
        .withKey(KeyCode.KeyR)
        .hasAction(async ctx => {
            const viewable = ctx.container.get(IWorkAreaService).activeViewable;
            if (viewable?.canRevert()) await viewable.revert();
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.runDocument, "Run")
        .withKey(KeyCode.F5)
        .firesEvent(async () => new (await import("@application/scripts/run-script-command")).RunScriptCommand())
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.cancelAndExecute, "Cancel and Execute")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.F5)
        .hasAction(async (ctx) => {
            const session = ctx.session;
            const environment = session.active;
            if (!environment) return;

            const scriptService = ctx.container.get(IScriptService);

            if (environment.status === "Running") {
                await scriptService.stop(environment.script.id, false);

                // Wait for the environment to leave the Running state before re-running.
                const deadline = Date.now() + 15_000;
                while (environment.status === "Running" && Date.now() < deadline) {
                    await new Promise(resolve => setTimeout(resolve, 100));
                }
            }

            await scriptService.run(environment.script.id, new RunOptions());
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openDocumentProperties, "Script Properties")
        .withKey(KeyCode.F4)
        .hasAction((ctx) => {
            if (ctx.session.active) {
                ctx.container.get(IScriptService).openConfigWindow(ctx.session.active.script.id, null);
            }
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openSettings, "Settings")
        .withKey(KeyCode.F12)
        .hasAction((ctx) => ctx.container.get(ISettingsService).openSettingsWindow(null))
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openOutput, "Output")
        .withCtrlKey()
        .withKey(KeyCode.KeyR)
        .firesEvent(async () => {
            const TogglePaneCommand = (await import("@application/panes/toggle-pane-command")).TogglePaneCommand;
            const OutputPane = (await import("../../../windows/main/panes")).OutputPane;

            return new TogglePaneCommand(OutputPane);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openExplorer, "Explorer")
        .withAltKey()
        .withKey(KeyCode.KeyE)
        .firesEvent(async () => {
            const TogglePaneCommand = (await import("@application/panes/toggle-pane-command")).TogglePaneCommand;
            const Explorer = (await import("../../../windows/main/panes")).Explorer;

            return new TogglePaneCommand(Explorer);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.openNamespaces, "Namespaces")
        .withAltKey()
        .withKey(KeyCode.KeyN)
        .firesEvent(async () => {
            const TogglePaneCommand = (await import("@application/panes/toggle-pane-command")).TogglePaneCommand;
            const NamespacesPane = (await import("../../../windows/main/panes")).NamespacesPane;

            return new TogglePaneCommand(NamespacesPane);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.reloadWindow, "Reload")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.KeyR)
        .hasAction(() => window.location.reload())
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.zoomIn, "Zoom In")
        .withCtrlKey()
        .withKey(KeyCode.Equal)
        .hasAction((ctx) => ctx.container.get(IWindowService).zoomIn())
        .captureDefaultKeyCombo()
        .configurable(false)
        .enabled(),

    new Shortcut(ShortcutIds.zoomOut, "Zoom Out")
        .withCtrlKey()
        .withKey(KeyCode.Minus)
        .hasAction((ctx) => ctx.container.get(IWindowService).zoomOut())
        .captureDefaultKeyCombo()
        .configurable(false)
        .enabled(),

    new Shortcut(ShortcutIds.zoomReset, "Zoom Reset")
        .withCtrlKey()
        .withKey(KeyCode.Digit0)
        .hasAction((ctx) => ctx.container.get(IWindowService).resetZoom())
        .captureDefaultKeyCombo()
        .configurable(false)
        .enabled(),

    new Shortcut(ShortcutIds.vimModeToggle, "Vim Mode")
        .withAltKey()
        .withKey(KeyCode.KeyV)
        .hasAction((ctx) => {
            const settings = ctx.container.get(Settings);
            settings.editor.vim.enabled = !settings.editor.vim.enabled;
            ctx.container.get(ISettingsService).update(settings);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.toggleBookmark, "Toggle Bookmark")
        .withCtrlKey()
        .withKey(KeyCode.F2)
        .hasAction(ctx => {
            const editor = ctx.container.get(ITextEditorService).active;
            const document = editor?.active;
            const line = editor?.monaco.getPosition()?.lineNumber;
            if (!document || line === undefined) return;
            getBookmarkManager(document.id).toggle(line);
            editor.refreshBookmarkDecorations(document);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.nextBookmark, "Next Bookmark")
        .withKey(KeyCode.F2)
        .hasAction(ctx => navigateBookmark(ctx.container.get(ITextEditorService).active, 1))
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.previousBookmark, "Previous Bookmark")
        .withShiftKey()
        .withKey(KeyCode.F2)
        .hasAction(ctx => navigateBookmark(ctx.container.get(ITextEditorService).active, -1))
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    new Shortcut(ShortcutIds.clearBookmarks, "Clear Bookmarks")
        .withCtrlKey()
        .withShiftKey()
        .withKey(KeyCode.F2)
        .hasAction(ctx => {
            const editor = ctx.container.get(ITextEditorService).active;
            const document = editor?.active;
            if (!document || !editor) return;
            getBookmarkManager(document.id).clear();
            editor.refreshBookmarkDecorations(document);
        })
        .captureDefaultKeyCombo()
        .configurable()
        .enabled(),

    ...Array.from({length: 10}, (_, number) => [
        new Shortcut(`shortcut.editor.bookmark.set.${number}`, `Set Bookmark ${number}`)
            .withCtrlKey()
            .withAltKey()
            .withKey(bookmarkNumberKeys[number])
            .hasAction(ctx => {
                const editor = ctx.container.get(ITextEditorService).active;
                const document = editor?.active;
                const line = editor?.monaco.getPosition()?.lineNumber;
                if (!document || line === undefined) return;
                getBookmarkManager(document.id).toggleNumber(number, line);
                editor.refreshBookmarkDecorations(document);
            })
            .captureDefaultKeyCombo()
            .configurable()
            .enabled(),
        new Shortcut(`shortcut.editor.bookmark.jump.${number}`, `Go to Bookmark ${number}`)
            .withAltKey()
            .withKey(bookmarkNumberKeys[number])
            .hasAction(ctx => {
                const editor = ctx.container.get(ITextEditorService).active;
                const document = editor?.active;
                const line = document ? getBookmarkManager(document.id).getNumber(number) : undefined;
                if (editor && line !== undefined) revealBookmark(editor, line);
            })
            .captureDefaultKeyCombo()
            .configurable()
            .enabled(),
    ]).flat(),
];

function navigateBookmark(editor: ITextEditor | undefined, direction: 1 | -1): void {
    const document = editor?.active;
    const currentLine = editor?.monaco.getPosition()?.lineNumber ?? 0;
    if (!editor || !document) return;
    const line = getBookmarkManager(document.id).next(currentLine, direction);
    if (line !== undefined) revealBookmark(editor, line);
}

function revealBookmark(editor: ITextEditor, line: number): void {
    editor.monaco.revealLineInCenter(line);
    editor.monaco.setPosition({lineNumber: line, column: 1});
    editor.focus();
}
