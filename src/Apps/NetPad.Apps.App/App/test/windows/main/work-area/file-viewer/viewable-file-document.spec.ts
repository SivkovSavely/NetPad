import {ViewableFileDocument} from "../../../../../src/windows/main/work-area/viewers/file-viewer/viewable-file-document";
import {INativeTextFileService} from "@application/dialogs/inative-text-file-service";
import {IWorkAreaService} from "../../../../../src/windows/main/work-area/work-area-service";
import {IFileDocumentDialogService} from "../../../../../src/windows/main/work-area/viewers/file-viewer/file-document-dialog-service";

describe("ViewableFileDocument", () => {
    const createViewable = () => {
        const nativeFiles = {
            saveTextFile: jest.fn().mockResolvedValue(undefined),
            readTextFile: jest.fn().mockResolvedValue("disk text"),
            saveTextFileAs: jest.fn().mockResolvedValue({handle: "handle-2", path: "/tmp/new.md"}),
        } as unknown as INativeTextFileService;
        const dialogs = {
            ask: jest.fn().mockResolvedValue({value: "revert"}),
            alert: jest.fn().mockResolvedValue(undefined),
        } as unknown as IFileDocumentDialogService;
        const workArea = {
            viewerHosts: {items: []},
            recordRecentFile: jest.fn(),
        } as unknown as IWorkAreaService;
        const viewable = new ViewableFileDocument(
            {handle: "handle-1", path: "/tmp/readme.md", text: "original"},
            nativeFiles,
            nativeFiles,
            dialogs,
            workArea,
        );
        return {viewable, nativeFiles, dialogs, workArea};
    };

    test("maps common text extensions to Monaco languages", () => {
        expect(ViewableFileDocument.getLanguage("C:\\work\\source.cs")).toBe("csharp");
        expect(ViewableFileDocument.getLanguage("notes.MD")).toBe("markdown");
        expect(ViewableFileDocument.getLanguage("data.json")).toBe("json");
        expect(ViewableFileDocument.getLanguage("unknown.bin")).toBe("plaintext");
    });

    test("normalizes Windows path identity without changing case-sensitive POSIX paths", () => {
        expect(ViewableFileDocument.idForPath("C:\\Work\\Notes.txt"))
            .toBe(ViewableFileDocument.idForPath("c:/work/notes.txt"));
        expect(ViewableFileDocument.idForPath("/Work/Notes.txt"))
            .not.toBe(ViewableFileDocument.idForPath("/work/notes.txt"));
    });

    test("tracks file dirtiness and marks clean only after saving", async () => {
        const {viewable, nativeFiles} = createViewable();
        await viewable.textDocument.setText("server", "edited");
        expect(viewable.isDirty).toBe(true);

        await expect(viewable.save()).resolves.toBe(true);
        expect(nativeFiles.saveTextFile).toHaveBeenCalledWith("handle-1", "edited");
        expect(viewable.isDirty).toBe(false);
        viewable.dispose();
    });

    test("revert confirms dirty changes and reloads the text model", async () => {
        const {viewable, nativeFiles, dialogs} = createViewable();
        await viewable.textDocument.setText("server", "edited");

        await expect(viewable.revert()).resolves.toBe(true);
        expect(dialogs.ask).toHaveBeenCalled();
        expect(nativeFiles.readTextFile).toHaveBeenCalledWith("handle-1");
        expect(viewable.textDocument.text).toBe("disk text");
        expect(viewable.isDirty).toBe(false);
        viewable.dispose();
    });

    test("save as updates the file identity and recent entry", async () => {
        const {viewable, workArea} = createViewable();
        await viewable.saveAs();

        expect(viewable.name).toBe("new.md");
        expect(viewable.path).toBe("/tmp/new.md");
        expect(workArea.recordRecentFile).toHaveBeenCalledWith({handle: "handle-2", path: "/tmp/new.md"});
        viewable.dispose();
    });
});
