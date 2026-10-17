import {BuiltinShortcuts, ShortcutIds} from "@application/shortcuts/builtin-shortcuts";

describe("built-in editor shortcuts", () => {
    test("registers feature search, benchmarking, document switching, file search, revert, and bookmarks", () => {
        const ids = BuiltinShortcuts.map(shortcut => shortcut.id);
        expect(ids).toEqual(expect.arrayContaining([
            ShortcutIds.featureSearchOpen,
            ShortcutIds.openDocumentSwitcher,
            ShortcutIds.searchFiles,
            ShortcutIds.benchmarkSelection,
            ShortcutIds.revertDocument,
            ShortcutIds.toggleBookmark,
            ShortcutIds.nextBookmark,
            ShortcutIds.previousBookmark,
            ShortcutIds.clearBookmarks,
        ]));

        const benchmarkShortcut = BuiltinShortcuts.find(shortcut => shortcut.id === ShortcutIds.benchmarkSelection);
        expect(benchmarkShortcut?.isConfigurable).toBe(true);
        expect(benchmarkShortcut?.action).toBeDefined();

        for (let number = 0; number < 10; number++) {
            expect(ids).toContain(`shortcut.editor.bookmark.set.${number}`);
            expect(ids).toContain(`shortcut.editor.bookmark.jump.${number}`);
        }
    });
});
