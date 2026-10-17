import {TextDocument} from "@application/editor/text-document";

describe("TextDocument edit history", () => {
    test("restores capped undo and redo snapshots across a new document instance", async () => {
        const source = new TextDocument("source", "plaintext", "one");
        await source.setText(source, "two");
        await source.setText(source, "three");

        const restored = new TextDocument("restored", "plaintext", source.text);
        restored.restoreHistoryState(source.getHistoryState());

        await restored.undo();
        expect(restored.text).toBe("two");
        await restored.undo();
        expect(restored.text).toBe("one");
        await restored.redo();
        expect(restored.text).toBe("two");
    });

    test("server changes do not create edits and reset clears history", async () => {
        const document = new TextDocument("doc", "plaintext", "one");
        await document.setText("server", "two");
        expect(document.canUndo).toBe(false);

        await document.setText(document, "three");
        expect(document.canUndo).toBe(true);
        document.resetHistory();
        expect(document.canUndo).toBe(false);
        expect(document.canRedo).toBe(false);
    });

    test("bounds the number of persisted snapshots", async () => {
        const document = new TextDocument("doc", "plaintext", "0");
        for (let index = 1; index <= 120; index++) {
            await document.setText(document, String(index));
        }

        expect(document.getHistoryState().undo.length).toBeLessThanOrEqual(100);
    });
});
