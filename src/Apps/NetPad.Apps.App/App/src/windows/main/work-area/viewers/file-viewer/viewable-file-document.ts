import {INativeTextFileService, ITextFileContentService, NativeTextFile, NativeTextFileReference} from "@application/dialogs/inative-text-file-service";
import {TextLanguage} from "@application/editor/text-language";
import {ViewerHost} from "../viewer-host";
import {ViewableTextDocument} from "../viewable-text-document";
import {IWorkAreaService} from "../../work-area-service";
import type {IFileDocumentDialogService} from "./file-document-dialog-service";

const textFileFilters = [
    {name: "Text Files", extensions: ["txt", "text", "cs", "json", "xml", "md", "markdown", "html", "htm", "css", "js", "ts", "yaml", "yml", "ini", "sql"]},
    {name: "All Files", extensions: ["*"]},
];

const languages: Record<string, TextLanguage> = {
    cs: "csharp",
    json: "json",
    xml: "xml",
    md: "markdown",
    markdown: "markdown",
    html: "html",
    htm: "html",
    css: "css",
    js: "javascript",
    ts: "typescript",
    yaml: "yaml",
    yml: "yaml",
    ini: "ini",
    sql: "sql",
};

export class ViewableFileDocument extends ViewableTextDocument {
    private handle: string;
    private filePath: string;
    private savedText: string;
    private source?: "library";
    public pendingLine?: number;

    constructor(
        file: NativeTextFile,
        private contentFileService: ITextFileContentService,
        private readonly nativeTextFileService: INativeTextFileService | undefined,
        private readonly dialogUtil: IFileDocumentDialogService,
        private readonly workAreaService: IWorkAreaService,
    ) {
        super(
            ViewableFileDocument.idForPath(file.path),
            ViewableFileDocument.getName(file.path),
            ViewableFileDocument.getLanguage(file.path),
            file.text,
        );
        this.handle = file.handle;
        this.filePath = file.path;
        this.savedText = file.text;
        this.source = file.source;
        this.path = file.path;
        this.tooltip = file.path;
    }

    public static idForPath(path: string): string {
        const normalized = path.replace(/\\/g, "/");
        const caseInsensitive = /^[a-z]:\//i.test(normalized) || normalized.startsWith("//");
        return `file:${caseInsensitive ? normalized.toLocaleLowerCase() : normalized}`;
    }

    public static getName(path: string): string {
        return path.replace(/\\/g, "/").split("/").pop() || path;
    }

    public static getLanguage(path: string): TextLanguage {
        const extension = ViewableFileDocument.getName(path).split(".").pop()?.toLowerCase() || "";
        return languages[extension] ?? "plaintext";
    }

    public override get name(): string {
        return ViewableFileDocument.getName(this.filePath);
    }

    public override get isDirty(): boolean {
        return this.savedText !== this.textDocument.text;
    }

    public override async open(viewerHost: ViewerHost): Promise<void> {
        viewerHost.addViewables(this);
    }

    public override async close(viewerHost: ViewerHost): Promise<void> {
        const openElsewhere = this.workAreaService.viewerHosts.items
            .some(host => host !== viewerHost && host.viewables.has(this));

        if (!openElsewhere && this.isDirty) {
            const result = await this.dialogUtil.ask({
                title: "Unsaved Changes",
                message: `Save changes to '${this.name}' before closing?`,
                buttons: [
                    {text: "Save", value: "save", isPrimary: true},
                    {text: "Don't Save", value: "discard"},
                    {text: "Cancel", value: "cancel"},
                ],
            });

            if (result?.value === "save") {
                if (!await this.save()) return;
            } else if (result?.value !== "discard") {
                return;
            }
        }

        viewerHost.removeViewables(this);
        if (!openElsewhere) this.dispose();
    }

    public override async activate(viewerHost: ViewerHost): Promise<void> {
        viewerHost.activate(this);
    }

    public override canSave(): boolean {
        return true;
    }

    public override async save(): Promise<boolean> {
        try {
            await this.contentFileService.saveTextFile(this.handle, this.textDocument.text);
            this.savedText = this.textDocument.text;
            return true;
        } catch (error) {
            await this.dialogUtil.alert({title: "Could Not Save File", message: String(error)});
            return false;
        }
    }

    public override canSaveAs(): boolean {
        return this.nativeTextFileService !== undefined;
    }

    public override async saveAs(): Promise<void> {
        try {
            if (!this.nativeTextFileService) return;
            const reference: NativeTextFileReference | null = await this.nativeTextFileService.saveTextFileAs(
                this.textDocument.text,
                {title: "Save Text File", defaultPath: this.filePath, filters: textFileFilters},
            );
            if (!reference) return;

            this.handle = reference.handle;
            this.filePath = reference.path;
            this.contentFileService = this.nativeTextFileService;
            this.source = undefined;
            this.path = reference.path;
            this.tooltip = reference.path;
            this.savedText = this.textDocument.text;
            this.workAreaService.recordRecentFile({...reference, source: this.source});
        } catch (error) {
            await this.dialogUtil.alert({title: "Could Not Save File", message: String(error)});
        }
    }

    public override canRevert(): boolean {
        return true;
    }

    public override async revert(): Promise<boolean> {
        if (this.isDirty) {
            const result = await this.dialogUtil.ask({
                title: "Revert File",
                message: `Discard changes and reload '${this.name}' from disk?`,
                buttons: [
                    {text: "Revert", value: "revert", isPrimary: true},
                    {text: "Cancel", value: "cancel"},
                ],
            });
            if (result?.value !== "revert") return false;
        }

        try {
            const text = await this.contentFileService.readTextFile(this.handle);
            await this.textDocument.setText("server", text);
            this.savedText = text;
            return true;
        } catch (error) {
            await this.dialogUtil.alert({title: "Could Not Revert File", message: String(error)});
            return false;
        }
    }
}

export {textFileFilters};
