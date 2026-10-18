import {watch} from "@aurelia/runtime-html";
import {IEventBus, IWindowService, ISession, Pane} from "@application";
import {CodeDecompilationResponse} from "@application/api";
import {ShowCodeDecompilationEvent} from "@application/code/show-code-decompilation-event";
import {AppWindows} from "@application/windows/app-windows";
import {IDisposable} from "@common";
import {LibraryTextFileService} from "@application/file-search/library-text-file-service";
import {IWorkAreaService} from "../../work-area/work-area-service";
import {tryOpenLoadedSource} from "./loaded-source-navigation";
import "./code-pane.scss";

export class CodePane extends Pane {
    public activeTab = "SyntaxTree";
    public decompilation?: CodeDecompilationResponse;
    private decompilationScriptId?: string;
    private decompilationSubscription?: IDisposable;

    constructor(
        @IWindowService private readonly windowService: IWindowService,
        private readonly appWindows: AppWindows,
        @IEventBus private readonly eventBus: IEventBus,
        @ISession private readonly session: ISession,
        @IWorkAreaService private readonly workAreaService: IWorkAreaService,
        private readonly libraryTextFileService: LibraryTextFileService) {
        super("Code", "code-icon");
    }

    public binding() {
        this.decompilationSubscription?.dispose();
        this.decompilationSubscription = this.eventBus.subscribe(ShowCodeDecompilationEvent, event => {
            if (event.scriptId !== this.session.active?.script.id) return;
            return this.showDecompilation(event.scriptId, event.response);
        });
    }

    private async showDecompilation(scriptId: string, response: CodeDecompilationResponse): Promise<void> {
        if (response.isSource && !response.isCurrentScriptSource && response.sourcePath) {
            const opened = await tryOpenLoadedSource(
                response.sourcePath,
                response.startLine,
                this.libraryTextFileService,
                this.workAreaService,
                () => scriptId === this.session.active?.script.id);
            if (opened || scriptId !== this.session.active?.script.id) return;
        }

        if (scriptId !== this.session.active?.script.id) return;
        this.decompilation = response;
        this.decompilationScriptId = scriptId;
        this.activeTab = "Decompiled";
        this.activate();
    }

    public detaching() {
        this.decompilationSubscription?.dispose();
        this.decompilationSubscription = undefined;
    }

    @watch<CodePane>(vm => vm.session.active?.script.id)
    private activeScriptChanged() {
        if (this.decompilationScriptId === this.session.active?.script.id) return;

        this.decompilation = undefined;
        this.decompilationScriptId = undefined;
        if (this.activeTab === "Decompiled") this.activeTab = "SyntaxTree";
    }

    private async openExternalCodeWindow() {
        this.hide();
        await this.windowService.openCodeWindow();
    }

    @watch<CodePane>(vm => vm.appWindows.items.map(x => x.name))
    private reactToExternalWindowState(currentWindowNames: string, previousWindowNames: string) {
        if (this.isWindow) {
            return;
        }

        const wasOpen = previousWindowNames.indexOf("code") >= 0;
        const currentlyOpen = currentWindowNames.indexOf("code") >= 0;
        const hostHasAnActivePaneOpen = this.host?.active;

        if (wasOpen && !currentlyOpen && !hostHasAnActivePaneOpen) {
            this.activate();
        } else if (currentlyOpen) {
            this.hide();
        }
    }
}
