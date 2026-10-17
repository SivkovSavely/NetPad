import {CancellationToken, editor, languages, Position, Range} from "monaco-editor";
import {ICodeService} from "@application/code/icode-service";
import {ShowCodeDecompilationEvent} from "@application/code/show-code-decompilation-event";
import {IEventBus} from "@application/events/ievent-bus";
import {MonacoEditorUtil} from "@application/editor/monaco/monaco-editor-util";
import {ISession} from "@application/sessions/isession";
import {CodeDecompilationRequest} from "@application/api";
import {IDefinitionProvider} from "./interfaces";

export class CodeDecompilationDefinitionProvider implements IDefinitionProvider {
    constructor(
        @ICodeService private readonly codeService: ICodeService,
        @IEventBus private readonly eventBus: IEventBus,
        @ISession private readonly session: ISession) {
    }

    public async provideDefinition(
        model: editor.ITextModel,
        position: Position,
        token: CancellationToken): Promise<languages.Definition | null> {
        if (model.uri.scheme !== "inmemory" || model.uri.authority !== "model") {
            return null;
        }

        const scriptId = MonacoEditorUtil.getScriptId(model);

        try {
            const response = await this.codeService.decompileAtPosition(
                scriptId,
                new CodeDecompilationRequest({line: position.lineNumber, column: position.column}),
                MonacoEditorUtil.abortSignalFrom(10000, token));

            if (!response || token.isCancellationRequested || this.session.active?.script.id !== scriptId) {
                return null;
            }

            if (response.isSource && response.isCurrentScriptSource) {
                return [{
                    uri: model.uri,
                    range: new Range(
                        response.startLine,
                        response.startColumn,
                        response.endLine,
                        response.endColumn)
                }];
            }

            this.eventBus.publish(new ShowCodeDecompilationEvent(scriptId, response));
            return [];
        } catch (error) {
            if (!token.isCancellationRequested) {
                console.error("Could not decompile metadata symbol", error);
            }
            return null;
        }
    }
}
