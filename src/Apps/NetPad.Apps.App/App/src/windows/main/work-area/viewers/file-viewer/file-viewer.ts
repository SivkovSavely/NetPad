import {ILogger} from "aurelia";
import {Viewer} from "../viewer";
import {ViewableObject} from "../viewable-object";
import {ViewableFileDocument} from "./viewable-file-document";
import {ITextEditor} from "@application/editor/text-editor";
import {renderMarkdown} from "@application/markdown/render-markdown";
import {Workbench} from "../../../workbench";
import {VimStatusbarItem} from "@application/editor/vim/vim-statusbar-item";
import {TextEditorCursorPositionStatusbarItem} from "@application/editor/text-editor-cursor-position-statusbar-item";
import {SubscriptionToken} from "@common";

export class FileViewer extends Viewer {
    public editor: ITextEditor;
    public previewEnabled = false;
    public previewHtml = "";

    private textChangeSubscription?: SubscriptionToken;
    private previewTimer?: ReturnType<typeof setTimeout>;
    private previewRevision = 0;

    constructor(
        private readonly workbench: Workbench,
        @ILogger logger: ILogger,
    ) {
        super(logger);
    }

    public attached(): void {
        if (this.viewable && (!this.editor.active || this.editor.active.id !== this.viewable.id)) {
            this.open(this.viewable as ViewableFileDocument);
        }

        this.workbench.statusbarService.addItem(TextEditorCursorPositionStatusbarItem, "right");
        this.addDisposable(() => this.workbench.statusbarService.removeItem(TextEditorCursorPositionStatusbarItem));
        this.workbench.statusbarService.addItem(VimStatusbarItem, "left");
        this.addDisposable(() => this.workbench.statusbarService.removeItem(VimStatusbarItem));
        this.addDisposable(() => this.clearPreviewSubscription());
    }

    public override canOpen(viewable: ViewableObject): boolean {
        return viewable instanceof ViewableFileDocument;
    }

    public open(viewable: ViewableFileDocument): void {
        if (!this.canOpen(viewable)) return;

        this.clearPreviewSubscription();
        this.viewable = viewable;
        this.previewEnabled = false;
        this.previewHtml = "";

        if (this.editor) this.editor.open(viewable.textDocument);

        this.textChangeSubscription = viewable.textDocument.onChange(async () => this.schedulePreview());
    }

    public close(viewable: ViewableFileDocument): void {
        this.editor.close(viewable.textDocument.id);
        if (this.viewable === viewable) {
            this.clearPreviewSubscription();
            this.previewEnabled = false;
            this.previewHtml = "";
        }
    }

    public get previewKind(): "markdown" | "html" | undefined {
        const fileName = this.viewable?.name.toLowerCase() ?? "";
        if (fileName.endsWith(".md") || fileName.endsWith(".markdown")) return "markdown";
        if (fileName.endsWith(".html") || fileName.endsWith(".htm")) return "html";
        return undefined;
    }

    public async togglePreview(): Promise<void> {
        if (!this.previewKind) return;
        this.previewEnabled = !this.previewEnabled;
        if (this.previewEnabled) await this.updatePreview();
    }

    private schedulePreview(): void {
        if (!this.previewEnabled || !this.previewKind) return;
        if (this.previewTimer) clearTimeout(this.previewTimer);
        this.previewTimer = setTimeout(() => {
            this.previewTimer = undefined;
            void this.updatePreview();
        }, 200);
    }

    private async updatePreview(): Promise<void> {
        const viewable = this.viewable;
        const kind = this.previewKind;
        if (!viewable || !kind || !this.previewEnabled) return;

        const revision = ++this.previewRevision;
        const source = viewable.textDocument.text;
        try {
            const html = kind === "markdown" ? await renderMarkdown(source) : source;
            if (revision === this.previewRevision && this.viewable === viewable && this.previewEnabled) {
                this.previewHtml = html;
            }
        } catch (error) {
            this.logger.error("Could not render file preview", error);
        }
    }

    private clearPreviewSubscription(): void {
        this.textChangeSubscription?.dispose();
        this.textChangeSubscription = undefined;
        if (this.previewTimer) clearTimeout(this.previewTimer);
        this.previewTimer = undefined;
        this.previewRevision++;
    }
}
