import {DI, ILogger} from "aurelia";
import {watch} from "@aurelia/runtime-html";
import * as monaco from "monaco-editor";
import {WithDisposables} from "@common";
import {IEventBus, MonacoEditorUtil, Settings, ViewModelBase} from "@application";
import {TextDocument} from "./text-document";
import {ITextEditorService} from "./itext-editor-service";
import {getBookmarkManager} from "./bookmark-manager";

export const ITextEditor = DI.createInterface<ITextEditor>();

export interface ITextEditor extends WithDisposables {
    monaco: monaco.editor.IStandaloneCodeEditor;
    position?: monaco.Position | null;
    active?: TextDocument | null;

    open(document: TextDocument): void;
    close(documentId: string): void;
    focus(): void;
    refreshBookmarkDecorations(document: TextDocument): void;
}

export class TextEditor extends ViewModelBase implements ITextEditor {
    public monaco: monaco.editor.IStandaloneCodeEditor;
    public position?: monaco.Position | null;
    public active?: TextDocument | null;

    private monacoEditorElement: HTMLElement;
    private viewStates = new Map<string, monaco.editor.ICodeEditorViewState | null>();
    private bookmarkDecorations = new Map<string, monaco.editor.IEditorDecorationsCollection>();

    constructor(
        private readonly settings: Settings,
        @ITextEditorService private readonly textEditorService: ITextEditorService,
        @IEventBus private readonly eventBus: IEventBus,
        @ILogger logger: ILogger) {
        super(logger);
    }

    public open(document: TextDocument) {
        this.ensureEditorInitialized();

        const currentOpen = this.active;
        if (currentOpen === document) {
            this.revealPendingLine(document);
            this.logger.warn(`Document is already open`);
            return;
        }

        if (currentOpen) {
            this.viewStates.set(currentOpen.id, this.monaco.saveViewState());
        }

        this.monaco.setModel(document.textModel);
        this.refreshBookmarkDecorations(document);

        this.monaco.restoreViewState(this.viewStates.get(document.id) || null);
        this.revealPendingLine(document);

        this.active = document;
    }

    public close(documentId: string) {
        this.ensureEditorInitialized();

        this.viewStates.delete(documentId);
        this.bookmarkDecorations.get(documentId)?.clear();
        this.bookmarkDecorations.delete(documentId);
        if (this.active && this.active.id === documentId) {
            this.monaco.setModel(null);
            this.active = null;
        }
    }

    public focus() {
        setTimeout(() => this.monaco.focus(), 50);
    }

    private ensureEditorInitialized() {
        if (this.monaco) return;
        this.initializeEditor();
    }

    private initializeEditor() {
        if (this.monaco) return;

        this.monaco = monaco.editor.create(this.monacoEditorElement, {
            model: null,
            "semanticHighlighting.enabled": true,
            formatOnType: true,
            formatOnPaste: true,
            automaticLayout: true,
            glyphMargin: true,
        });

        this.updateEditorSettings();

        this.addDisposable(this.monaco.onDidFocusEditorText(() => {
            if (this.textEditorService.active !== this) {
                this.textEditorService.active = this;
            }
        }));

        this.addDisposable(this.monaco.onDidDispose(() => {
            if (this.textEditorService.active === this) {
                this.textEditorService.active = undefined;
            }
        }));

        this.addDisposable(
            this.monaco.onDidChangeCursorSelection(ev => {
                if (this.active) {
                    this.active.selection = ev.selection;
                }
            })
        );

        this.addDisposable(
            this.monaco.onDidChangeCursorPosition(ev => this.position = ev.position)
        );

        this.addDisposable(this.monaco.onDidChangeModelContent(() => {
            const document = this.active;
            const decorations = document && this.bookmarkDecorations.get(document.id);
            if (document && decorations) {
                getBookmarkManager(document.id).updateTrackedLines(
                    decorations.getRanges().map(range => range.startLineNumber),
                );
            }
        }));

        this.addDisposable(
            this.monaco.onDidChangeModel(ev => {
                this.position = this.monaco.getPosition();
                if (this.active && this.monaco.getModel() === this.active.textModel && !this.bookmarkDecorations.has(this.active.id)) this.refreshBookmarkDecorations(this.active);
            })
        );

        // Defer grabbing current position
        setTimeout(() => this.position = this.monaco.getPosition());

        this.focus();

        this.addDisposable(() => {
            this.viewStates.clear();
            for (const decorations of this.bookmarkDecorations.values()) decorations.clear();
            this.bookmarkDecorations.clear();
            this.monaco.dispose();
        });
    }

    public refreshBookmarkDecorations(document: TextDocument) {
        const model = this.monaco.getModel();
        if (!model || model !== document.textModel) return;
        this.bookmarkDecorations.get(document.id)?.clear();
        const decorations = getBookmarkManager(document.id).decorations(model);
        this.bookmarkDecorations.set(document.id, this.monaco.createDecorationsCollection(decorations));
    }

    private revealPendingLine(document: TextDocument): void {
        const line = document.pendingLine;
        const model = this.monaco.getModel();
        if (line === undefined || !model) return;
        const targetLine = Math.max(1, Math.min(line, model.getLineCount()));
        this.monaco.revealLineInCenter(targetLine);
        this.monaco.setPosition({lineNumber: targetLine, column: 1});
        document.pendingLine = undefined;
    }

    @watch<TextEditor>(vm => vm.settings.appearance.theme)
    @watch<TextEditor>(vm => vm.settings.editor.monacoOptions)
    private async updateEditorSettings() {
        if (!this.monaco) return;
        await MonacoEditorUtil.updateOptions(this.monaco, this.settings);
    }
}
