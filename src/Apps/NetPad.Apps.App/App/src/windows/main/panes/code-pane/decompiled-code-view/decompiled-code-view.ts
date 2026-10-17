import {bindable} from "aurelia";
import * as monaco from "monaco-editor";
import {CodeDecompilationResponse} from "@application/api";
import "./decompiled-code-view.scss";

let nextModelId = 0;

export class DecompiledCodeView {
    @bindable public response?: CodeDecompilationResponse | null;
    @bindable public isActive = false;

    private editorElement: HTMLElement;
    private editor?: monaco.editor.IStandaloneCodeEditor;
    private model?: monaco.editor.ITextModel;

    public attached() {
        this.editor = monaco.editor.create(this.editorElement, {
            automaticLayout: true,
            contextmenu: false,
            minimap: {enabled: false},
            readOnly: true,
            domReadOnly: true,
            scrollBeyondLastLine: false
        });
        this.setResponse();
    }

    public detaching() {
        this.editor?.dispose();
        this.model?.dispose();
    }

    private responseChanged() {
        this.setResponse();
    }

    private isActiveChanged() {
        if (this.isActive) {
            this.editor?.layout();
            this.revealSymbol();
        }
    }

    private setResponse() {
        if (!this.editor || !this.response) return;

        const sourcePath = this.response.isSource ? this.response.sourcePath : undefined;
        const model = monaco.editor.createModel(
            this.response.content,
            this.response.language || "plaintext",
            monaco.Uri.from({
                scheme: sourcePath ? "netpad-source" : "netpad-decompiled",
                path: `/${sourcePath ? "source" : "metadata"}-${++nextModelId}`,
                query: sourcePath || ""
            }));
        const previousModel = this.model;
        this.model = model;
        this.editor.setModel(model);
        previousModel?.dispose();
        this.revealSymbol();
    }

    private revealSymbol() {
        if (!this.editor || !this.response || !this.model) return;

        const range = new monaco.Range(
            this.response.startLine,
            this.response.startColumn,
            this.response.endLine,
            this.response.endColumn);
        this.editor.setSelection(range);
        this.editor.revealRangeInCenter(range);
    }
}
