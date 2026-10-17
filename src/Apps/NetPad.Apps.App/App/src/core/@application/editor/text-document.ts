import {SubscriptionToken, WithDisposables} from "@common";
import * as monaco from "monaco-editor";
import {MonacoEditorUtil} from "@application/editor/monaco/monaco-editor-util";
import {TextLanguage} from "@application/editor/text-language";

export type TextChangeSetter = TextDocument | "server";

export class TextDocument extends WithDisposables {
    private static readonly maxHistoryEntries = 100;
    private static readonly maxHistoryCharacters = 2_000_000;

    public selection: monaco.Selection | null | undefined;
    public pendingLine?: number;
    public viewState?: unknown;

    private _language: TextLanguage;
    private _text: string;
    private changeHandlers = new Set<(setter: TextChangeSetter, newText: string) => Promise<void>>();
    private _textModel?: monaco.editor.ITextModel;
    private undoHistory: string[] = [];
    private redoHistory: string[] = [];
    private replayingHistory = false;

    constructor(public readonly id: string, language: TextLanguage, text: string) {
        super();
        if (!id) throw new Error(`${nameof(TextDocument)} id cannot be empty.`);
        this._language = language;
        this._text = text ?? ""
    }

    public get language(): TextLanguage {
        return this._language;
    }

    public get textModel(): monaco.editor.ITextModel {
        if (!this._textModel) {
            const language = this.language || "plaintext";
            const initialText = this._text;

            this._textModel = monaco.editor.createModel(
                initialText,
                language,
                MonacoEditorUtil.constructModelUri(this.id)
            );

            this.addDisposable(this._textModel.onDidChangeContent(async () => {
                if (this._textModel)
                    await this.setText(this, this._textModel.getValue());
            }));

            this.onChange(async (setter, newValue) => {
                if (this === setter || !this._textModel) return;
                this._textModel.setValue(newValue);
            });
        }

        return this._textModel;
    }

    public get text(): string {
        return this._text;
    }

    public async setText(setter: TextChangeSetter, newValue: string) {
        if (this._text === newValue) return;

        if (setter === this && !this.replayingHistory) {
            this.undoHistory.push(this._text);
            this.redoHistory = [];
            this.trimHistory();
        }

        this._text = newValue ?? "";

        for (const changeHandler of this.changeHandlers) {
            try {
                await changeHandler(setter, newValue);
            } catch (ex) {
                console.error(`Error while calling change handler on ${nameof(TextDocument)}`, changeHandler, ex);
            }
        }
    }

    public onChange(handler: (setter: TextChangeSetter, newValue: string) => Promise<void>): SubscriptionToken {
        const wrappedHandler = (setter: TextChangeSetter, newValue: string) => handler(setter, newValue);
        this.changeHandlers.add(wrappedHandler);
        return new SubscriptionToken(() => this.changeHandlers.delete(wrappedHandler));
    }

    public get canUndo(): boolean {
        return this.undoHistory.length > 0;
    }

    public get canRedo(): boolean {
        return this.redoHistory.length > 0;
    }

    public async undo(): Promise<void> {
        const previous = this.undoHistory.pop();
        if (previous === undefined) return;
        this.redoHistory.push(this._text);
        await this.applyHistoryText(previous);
    }

    public async redo(): Promise<void> {
        const next = this.redoHistory.pop();
        if (next === undefined) return;
        this.undoHistory.push(this._text);
        await this.applyHistoryText(next);
    }

    public getHistoryState(): {undo: string[]; redo: string[]} {
        return {undo: [...this.undoHistory], redo: [...this.redoHistory]};
    }

    public restoreHistoryState(state: {undo?: string[]; redo?: string[]}): void {
        this.undoHistory = [...(state.undo ?? [])].filter(value => typeof value === "string");
        this.redoHistory = [...(state.redo ?? [])].filter(value => typeof value === "string");
        this.trimHistory();
    }

    public resetHistory(): void {
        this.undoHistory = [];
        this.redoHistory = [];
    }

    private async applyHistoryText(value: string): Promise<void> {
        this.replayingHistory = true;
        try {
            if (this._textModel) {
                this._textModel.setValue(value);
            } else {
                await this.setText(this, value);
            }
        } finally {
            this.replayingHistory = false;
            this.trimHistory();
        }
    }

    private trimHistory(): void {
        const totalCharacters = () => [...this.undoHistory, ...this.redoHistory]
            .reduce((total, text) => total + text.length, 0);

        while (this.undoHistory.length + this.redoHistory.length > TextDocument.maxHistoryEntries
            || totalCharacters() > TextDocument.maxHistoryCharacters) {
            if (this.undoHistory.length > 0) this.undoHistory.shift();
            else if (this.redoHistory.length > 0) this.redoHistory.shift();
            else break;
        }
    }

    public changeLanguage(language: TextLanguage) {
        this._language = language;
        // Skip the Monaco update if no model exists yet
        if (this._textModel) {
            monaco.editor.setModelLanguage(this._textModel, language);
        }
    }

    public override toString() {
        return `${this.constructor.name}: ${this.id}`;
    }

    public override dispose(): void {
        super.dispose();
        this.changeHandlers.clear();
        this._textModel?.dispose();
        this._textModel = undefined;
    }
}
