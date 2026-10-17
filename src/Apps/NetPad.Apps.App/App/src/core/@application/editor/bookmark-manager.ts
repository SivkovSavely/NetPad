import * as monaco from "monaco-editor";

export class BookmarkManager {
    private readonly lines = new Set<number>();
    private readonly numbered = new Map<number, number>();

    constructor(public readonly documentId: string) {}

    public getBookmarks(): number[] {
        return [...this.lines].sort((a, b) => a - b);
    }

    public toggle(line: number): boolean {
        if (!Number.isInteger(line) || line < 1) return false;
        if (this.lines.has(line)) {
            this.lines.delete(line);
            for (const [number, bookmarkLine] of this.numbered) {
                if (bookmarkLine === line) this.numbered.delete(number);
            }
            return false;
        }
        this.lines.add(line);
        return true;
    }

    public clear(): void {
        this.lines.clear();
        this.numbered.clear();
    }

    public next(line: number, direction: 1 | -1 = 1): number | undefined {
        const lines = this.getBookmarks();
        if (direction === 1) return lines.find(x => x > line) ?? lines[0];
        return [...lines].reverse().find(x => x < line) ?? lines[lines.length - 1];
    }

    public setNumber(number: number, line: number): void {
        if (!Number.isInteger(number) || number < 0 || number > 9) throw new RangeError("Bookmark number must be 0-9");
        if (!Number.isInteger(line) || line < 1) throw new RangeError("Line must be positive");
        this.lines.add(line);
        this.numbered.set(number, line);
    }

    public toggleNumber(number: number, line: number): void {
        if (this.getNumber(number) === line) {
            this.numbered.delete(number);
            if (![...this.numbered.values()].includes(line)) this.lines.delete(line);
            return;
        }
        this.setNumber(number, line);
    }

    public getNumber(number: number): number | undefined {
        return this.numbered.get(number);
    }

    public updateTrackedLines(trackedLines: number[]): void {
        const oldLines = this.getBookmarks();
        const newLines = trackedLines.filter(line => Number.isInteger(line) && line > 0);
        const updated = new Map(oldLines.map((line, index) => [line, newLines[index]]));
        this.lines.clear();
        for (const line of newLines) this.lines.add(line);
        for (const [number, line] of this.numbered) {
            const tracked = updated.get(line);
            if (tracked === undefined) this.numbered.delete(number);
            else this.numbered.set(number, tracked);
        }
    }

    public decorations(model: monaco.editor.ITextModel): monaco.editor.IModelDeltaDecoration[] {
        return this.getBookmarks().filter(line => line <= model.getLineCount()).map(line => {
            const number = [...this.numbered].find(([, numberedLine]) => numberedLine === line)?.[0];
            return {
            range: new monaco.Range(line, 1, line, 1),
            options: {
                isWholeLine: true,
                glyphMarginClassName: `netpad-bookmark-glyph${number === undefined ? "" : ` netpad-bookmark-number-${number}`}`,
                glyphMarginHoverMessage: {value: number === undefined ? "Bookmark" : `Bookmark ${number}`},
                stickiness: monaco.editor.TrackedRangeStickiness.GrowsOnlyWhenTypingAfter,
            }
        };
        });
    }
}

const managers = new Map<string, BookmarkManager>();

export function getBookmarkManager(documentId: string): BookmarkManager {
    let manager = managers.get(documentId);
    if (!manager) managers.set(documentId, manager = new BookmarkManager(documentId));
    return manager;
}
