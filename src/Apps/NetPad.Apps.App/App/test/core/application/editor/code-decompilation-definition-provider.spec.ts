import {Range} from "monaco-editor";
import type {CancellationToken, editor, Position} from "monaco-editor";
import {CodeDecompilationResponse} from "@application/api";
import {ICodeService} from "@application/code/icode-service";
import {ShowCodeDecompilationEvent} from "@application/code/show-code-decompilation-event";
import {IEventBus} from "@application/events/ievent-bus";
import {MonacoEditorUtil} from "@application/editor/monaco/monaco-editor-util";
import {ISession} from "@application/sessions/isession";
import {CodeDecompilationDefinitionProvider} from "@application/editor/providers/code-decompilation-definition-provider";

describe("CodeDecompilationDefinitionProvider", () => {
    const response = {
        language: "csharp",
        content: "class ExternalType {}",
        assemblyIdentity: "ExternalAssembly",
        symbolIdentity: "ExternalType",
        startLine: 1,
        startColumn: 7,
        endLine: 1,
        endColumn: 19,
        isSource: false,
        isCurrentScriptSource: false
    } as CodeDecompilationResponse;

    let decompileAtPosition: jest.Mock;
    let publish: jest.Mock;
    let provider: CodeDecompilationDefinitionProvider;
    let activeScriptId: string | undefined;

    beforeEach(() => {
        decompileAtPosition = jest.fn();
        publish = jest.fn();
        activeScriptId = "script-1";
        provider = new CodeDecompilationDefinitionProvider(
            {decompileAtPosition} as unknown as ICodeService,
            {publish} as unknown as IEventBus,
            {get active() { return activeScriptId ? {script: {id: activeScriptId}} : undefined; }} as unknown as ISession);
        jest.spyOn(MonacoEditorUtil, "getScriptId").mockReturnValue("script-1");
        jest.spyOn(MonacoEditorUtil, "abortSignalFrom").mockReturnValue(new AbortController().signal);
    });

    afterEach(() => jest.restoreAllMocks());

    const model = () => ({
        uri: {scheme: "inmemory", authority: "model", path: "/script-1"}
    } as editor.ITextModel);
    const position = {lineNumber: 4, column: 12} as Position;
    const token = {isCancellationRequested: false} as CancellationToken;

    it("returns null when there is no symbol so normal definition providers can run", async () => {
        decompileAtPosition.mockResolvedValue(null);

        const result = await provider.provideDefinition(model(), position, token);

        expect(result).toBeNull();
        expect(decompileAtPosition).toHaveBeenCalledWith(
            "script-1",
            {line: 4, column: 12},
            expect.any(AbortSignal));
        expect(publish).not.toHaveBeenCalled();
    });

    it("returns a Monaco location for a symbol in the current script", async () => {
        const sourceResponse = {
            ...response,
            isSource: true,
            isCurrentScriptSource: true,
            startLine: 2,
            startColumn: 3,
            endLine: 2,
            endColumn: 12
        } as CodeDecompilationResponse;
        decompileAtPosition.mockResolvedValue(sourceResponse);
        const sourceModel = model();

        const result = await provider.provideDefinition(sourceModel, position, token);

        expect(result).toEqual([{
            uri: sourceModel.uri,
            range: new Range(2, 3, 2, 12)
        }]);
        expect(publish).not.toHaveBeenCalled();
    });

    it("opens loaded source files in the Code pane", async () => {
        const sourceResponse = {
            ...response,
            isSource: true,
            isCurrentScriptSource: false,
            sourcePath: "/tmp/Loaded.cs",
            content: "class LoadedType {}"
        } as CodeDecompilationResponse;
        decompileAtPosition.mockResolvedValue(sourceResponse);

        const result = await provider.provideDefinition(model(), position, token);

        expect(result).toEqual([]);
        expect(publish).toHaveBeenCalledWith(new ShowCodeDecompilationEvent("script-1", sourceResponse));
    });

    it("publishes metadata decompilation and suppresses normal location navigation", async () => {
        decompileAtPosition.mockResolvedValue(response);

        const result = await provider.provideDefinition(model(), position, token);

        expect(result).toEqual([]);
        expect(publish).toHaveBeenCalledWith(new ShowCodeDecompilationEvent("script-1", response));
    });

    it("ignores a result after its script is no longer active", async () => {
        decompileAtPosition.mockImplementation(async () => {
            activeScriptId = "script-2";
            return response;
        });

        const result = await provider.provideDefinition(model(), position, token);

        expect(result).toBeNull();
        expect(publish).not.toHaveBeenCalled();
    });
});
