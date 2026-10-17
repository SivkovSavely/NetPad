import {watch} from "@aurelia/runtime-html";
import {IEventBus, IWindowService, ISession, Pane} from "@application";
import {CodeDecompilationResponse} from "@application/api";
import {ShowCodeDecompilationEvent} from "@application/code/show-code-decompilation-event";
import {AppWindows} from "@application/windows/app-windows";
import {IDisposable} from "@common";
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
        @ISession private readonly session: ISession) {
        super("Code", "code-icon");
    }

    public binding() {
        this.decompilationSubscription?.dispose();
        this.decompilationSubscription = this.eventBus.subscribe(ShowCodeDecompilationEvent, event => {
            if (event.scriptId !== this.session.active?.script.id) return;

            this.decompilation = event.response;
            this.decompilationScriptId = event.scriptId;
            this.activeTab = "Decompiled";
            this.activate();
        });
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
