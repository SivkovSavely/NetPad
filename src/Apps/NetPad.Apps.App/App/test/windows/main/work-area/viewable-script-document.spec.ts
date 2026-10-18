import {IEventBus} from "../../../../src/core/@application/events/ievent-bus";
import {IAppService} from "../../../../src/core/@application/app/iapp-service";
import {IScriptService} from "../../../../src/core/@application/scripts/iscript-service";
import {ISession} from "../../../../src/core/@application/sessions/isession";
import {Script, ScriptConfig, ScriptEnvironment} from "../../../../src/core/@application/api";
import {IFileDocumentDialogService} from "../../../../src/windows/main/work-area/viewers/file-viewer/file-document-dialog-service";
import {IWorkAreaService} from "../../../../src/windows/main/work-area/work-area-service";
import {ViewableScriptDocument} from "../../../../src/windows/main/work-area/viewers/script-viewer/viewable-script-document";

function makeScript(code: string, isDirty: boolean): Script {
    return new Script({
        id: "script-id",
        name: "Example",
        path: "/scripts/Example.netpad",
        isNew: false,
        config: new ScriptConfig({
            kind: "Program" as any,
            targetFrameworkVersion: "net9.0" as any,
            optimizationLevel: "Debug" as any,
            useAspNet: false,
            checkedArithmetic: true,
            namespaces: [],
            references: [],
        }),
        code,
        isDirty,
    });
}

function createViewable(isDirty: boolean, confirmation = "revert") {
    const script = makeScript("buffer", isDirty);
    const revertedScript = makeScript("disk", false);
    revertedScript.config.kind = "SQL" as any;
    const scriptService = {revertFromDisk: jest.fn().mockResolvedValue(revertedScript)};
    const dialogUtil = {
        ask: jest.fn().mockResolvedValue({value: confirmation}),
        alert: jest.fn().mockResolvedValue(undefined),
    };
    const eventBus = {subscribeToServer: jest.fn(() => ({dispose: jest.fn()}))};
    const viewable = new ViewableScriptDocument(
        {script, status: "Ready"} as unknown as ScriptEnvironment,
        scriptService as unknown as IScriptService,
        {} as ISession,
        {} as IAppService,
        {} as IWorkAreaService,
        dialogUtil as unknown as IFileDocumentDialogService,
        eventBus as unknown as IEventBus,
    );
    return {script, revertedScript, scriptService, dialogUtil, viewable};
}

describe("ViewableScriptDocument.revert", () => {
    it("confirms dirty scripts, restores code and config, and resets edit history", async () => {
        const {script, revertedScript, scriptService, dialogUtil, viewable} = createViewable(true);
        viewable.textDocument.restoreHistoryState({undo: ["older"], redo: ["newer"]});

        await expect(viewable.revert()).resolves.toBe(true);

        expect(dialogUtil.ask).toHaveBeenCalledWith(expect.objectContaining({title: "Revert Script"}));
        expect(scriptService.revertFromDisk).toHaveBeenCalledWith(script.id);
        expect(viewable.textDocument.text).toBe("disk");
        expect(script.code).toBe("disk");
        expect(script.config.kind).toBe("SQL");
        expect(script.isDirty).toBe(false);
        expect(viewable.textDocument.canUndo).toBe(false);
        expect(viewable.textDocument.canRedo).toBe(false);
        viewable.dispose();
    });

    it("does not read disk when a dirty revert is cancelled", async () => {
        const {scriptService, dialogUtil, viewable} = createViewable(true, "cancel");

        await expect(viewable.revert()).resolves.toBe(false);

        expect(dialogUtil.ask).toHaveBeenCalledTimes(1);
        expect(scriptService.revertFromDisk).not.toHaveBeenCalled();
        expect(viewable.textDocument.text).toBe("buffer");
        viewable.dispose();
    });

    it("does not ask for confirmation when the script is clean", async () => {
        const {dialogUtil, viewable} = createViewable(false);

        await expect(viewable.revert()).resolves.toBe(true);

        expect(dialogUtil.ask).not.toHaveBeenCalled();
        expect(viewable.textDocument.text).toBe("disk");
        viewable.dispose();
    });

    it("keeps the buffer when disk reload fails", async () => {
        const {scriptService, dialogUtil, viewable} = createViewable(false);
        scriptService.revertFromDisk.mockRejectedValue(new Error("file was deleted"));

        await expect(viewable.revert()).resolves.toBe(false);

        expect(viewable.textDocument.text).toBe("buffer");
        expect(dialogUtil.alert).toHaveBeenCalledWith(expect.objectContaining({title: "Could Not Revert Script"}));
        viewable.dispose();
    });
});
