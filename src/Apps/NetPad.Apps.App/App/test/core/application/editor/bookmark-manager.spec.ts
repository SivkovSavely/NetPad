import {BookmarkManager, getBookmarkManager} from "@application/editor/bookmark-manager";

describe("BookmarkManager", () => {
    test("toggles lines, traverses in either direction, and clears", () => {
        const manager = new BookmarkManager("doc");
        manager.toggle(8);
        manager.toggle(2);
        expect(manager.getBookmarks()).toEqual([2, 8]);
        expect(manager.next(2)).toBe(8);
        expect(manager.next(8)).toBe(2);
        expect(manager.next(8, -1)).toBe(2);
        expect(manager.toggle(2)).toBe(false);
        manager.clear();
        expect(manager.getBookmarks()).toEqual([]);
    });

    test("stores numbered bookmarks per document identity", () => {
        const first = getBookmarkManager("script-id");
        first.clear();
        first.setNumber(0, 4);
        expect(getBookmarkManager("script-id").getNumber(0)).toBe(4);
        expect(getBookmarkManager("other-doc").getNumber(0)).toBeUndefined();
    });

    test("rejects invalid bookmark numbers", () => {
        expect(() => new BookmarkManager("doc").setNumber(10, 1)).toThrow(RangeError);
    });

    test("tracks ordinary and numbered bookmarks through line edits", () => {
        const manager = new BookmarkManager("doc");
        manager.toggle(3);
        manager.setNumber(8, 9);

        manager.updateTrackedLines([5, 11]);

        expect(manager.getBookmarks()).toEqual([5, 11]);
        expect(manager.getNumber(8)).toBe(11);
    });

    test("toggling a numbered bookmark removes it", () => {
        const manager = new BookmarkManager("doc");
        manager.toggleNumber(4, 7);
        manager.toggleNumber(4, 7);
        expect(manager.getNumber(4)).toBeUndefined();
        expect(manager.getBookmarks()).toEqual([]);
    });

    test("round trips ordinary and numbered bookmark state", () => {
        const source = new BookmarkManager("source");
        source.toggle(3);
        source.setNumber(7, 12);

        const restored = new BookmarkManager("restored");
        restored.restoreState(source.getState());

        expect(restored.getBookmarks()).toEqual([3, 12]);
        expect(restored.getNumber(7)).toBe(12);
    });
});
