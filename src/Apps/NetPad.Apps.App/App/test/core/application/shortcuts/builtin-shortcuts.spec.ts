import {BuiltinShortcuts, ShortcutIds} from "@application/shortcuts/builtin-shortcuts";

describe("built-in editor shortcuts", () => {
    test("registers feature search, benchmarking, document switching, file search, revert, and bookmarks", () => {
        const ids = BuiltinShortcuts.map(shortcut => shortcut.id);
        expect(ids).toEqual(expect.arrayContaining([
            ShortcutIds.featureSearchOpen,
            ShortcutIds.openDocumentSwitcher,
            ShortcutIds.searchFiles,
            ShortcutIds.benchmarkSelection,
            ShortcutIds.runEntryPoint1,
            ShortcutIds.runEntryPoint2,
            ShortcutIds.runEntryPoint3,
            ShortcutIds.runEntryPoint4,
            ShortcutIds.runEntryPoint5,
            ShortcutIds.runEntryPoint6,
            ShortcutIds.runEntryPoint7,
            ShortcutIds.runEntryPoint8,
            ShortcutIds.runEntryPoint9,
            ShortcutIds.revertDocument,
            ShortcutIds.toggleBookmark,
            ShortcutIds.nextBookmark,
            ShortcutIds.previousBookmark,
            ShortcutIds.clearBookmarks,
        ]));

        const benchmarkShortcut = BuiltinShortcuts.find(shortcut => shortcut.id === ShortcutIds.benchmarkSelection);
        expect(benchmarkShortcut?.isConfigurable).toBe(true);
        expect(benchmarkShortcut?.action).toBeDefined();

        for (const entryPoint of [
            ShortcutIds.runEntryPoint1, ShortcutIds.runEntryPoint2, ShortcutIds.runEntryPoint3,
            ShortcutIds.runEntryPoint4, ShortcutIds.runEntryPoint5, ShortcutIds.runEntryPoint6,
            ShortcutIds.runEntryPoint7, ShortcutIds.runEntryPoint8, ShortcutIds.runEntryPoint9,
        ]) {
            const shortcut = BuiltinShortcuts.find(item => item.id === entryPoint);
            expect(shortcut?.isConfigurable).toBe(true);
            expect(shortcut?.action).toBeDefined();
        }

        for (let number = 0; number < 10; number++) {
            expect(ids).toContain(`shortcut.editor.bookmark.set.${number}`);
            expect(ids).toContain(`shortcut.editor.bookmark.jump.${number}`);
        }
    });
});
