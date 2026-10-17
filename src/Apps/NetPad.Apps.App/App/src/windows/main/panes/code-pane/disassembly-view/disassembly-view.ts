import {bindable, ILogger} from "aurelia";
import {watch} from "@aurelia/runtime-html";
import {
    CodePaneContent,
    ICodeService,
    IEventBus,
    ISession,
    Pane,
    ScriptCodeUpdatedEvent,
    ScriptConfigPropertyChangedEvent,
    ScriptPropertyChangedEvent,
    ViewModelBase
} from "@application";
import {Util} from "@common";
import {UiUtil} from "@common/utils/ui-util";
import {getDisassemblyErrorMessage} from "./disassembly-error-message";
import hljs from "highlight.js/lib/common";
import x86asm from "highlight.js/lib/languages/x86asm";
import "highlight.js/styles/monokai.min.css";

if (!hljs.getLanguage("asm")) {
    hljs.registerLanguage("asm", x86asm);
}

export class DisassemblyView extends ViewModelBase {
    private current: string | null;
    private message?: string;
    private loading = false;
    private requestVersion = 0;
    private abortController?: AbortController;

    @bindable public pane: Pane;
    @bindable public isActive: boolean;

    constructor(private readonly element: Element,
                @ISession private readonly session: ISession,
                @ICodeService private readonly codeService: ICodeService,
                @IEventBus private readonly eventBus: IEventBus,
                @ILogger logger: ILogger) {
        super(logger);
    }

    public attached() {
        this.addDisposable(UiUtil.confineSelectAllToElement(this.element));
        this.refresh();
        this.addDisposable(this.eventBus.subscribe(ScriptCodeUpdatedEvent, ev => {
            if (ev.scriptId === this.session.active?.script.id) this.refresh();
        }));
        this.addDisposable(this.eventBus.subscribeToServer(ScriptCodeUpdatedEvent, ev => {
            if (ev.scriptId === this.session.active?.script.id) this.refresh();
        }));
        this.addDisposable(this.eventBus.subscribeToServer(ScriptPropertyChangedEvent, ev => {
            if (ev.scriptId === this.session.active?.script.id) this.refresh();
        }));
        this.addDisposable(this.eventBus.subscribeToServer(ScriptConfigPropertyChangedEvent, ev => {
            if (ev.scriptId === this.session.active?.script.id) this.refresh();
        }));
    }

    @watch<DisassemblyView>(vm => vm.pane.isOpen)
    private paneViewModeChanged() {
        this.refresh();
    }

    @watch<DisassemblyView>(vm => vm.session.active)
    private activeScriptChanged() {
        this.refresh();
    }

    private isActiveChanged() {
        this.refresh();
    }

    private refresh() {
        this.requestVersion++;
        this.abortController?.abort();
        this.loading = false;
        this.loadDisassembly();
    }

    private loadDisassembly = Util.debounceAsync(this, async () => {
        if (!this.pane?.isOpen || !this.isActive) {
            return;
        }

        const version = this.requestVersion;
        const script = this.session.active?.script;
        this.current = null;
        this.message = undefined;

        if (!script) {
            this.message = "Open a script to view native JIT disassembly.";
            return;
        }

        if (!script.code?.trim()) {
            this.message = "Enter code to generate native JIT disassembly.";
            return;
        }

        const controller = new AbortController();
        this.abortController = controller;
        this.loading = true;

        try {
            const response = await this.codeService.getNativeDisassembly(script.id, controller.signal);
            if (!this.isCurrentRequest(version, script.id, controller)) return;

            if (response.supported === false) {
                this.message = response.error || "Native JIT disassembly is not supported on this platform.";
            } else if (response.error) {
                this.message = response.error;
            } else if (!response.content?.trim()) {
                this.message = "No native JIT disassembly was produced.";
            } else {
                this.current = this.highlight(response);
            }
        } catch (error) {
            if (!this.isCurrentRequest(version, script.id, controller)) return;
            this.message = getDisassemblyErrorMessage(error);
        } finally {
            if (this.isCurrentRequest(version, script.id, controller)) {
                this.loading = false;
                this.abortController = undefined;
            }
        }
    }, 500, true);

    private isCurrentRequest(version: number, scriptId: string, controller: AbortController): boolean {
        return !controller.signal.aborted
            && version === this.requestVersion
            && this.session.active?.script.id === scriptId;
    }

    private highlight(response: CodePaneContent): string {
        const language = response.language?.toLowerCase();
        if (language && hljs.getLanguage(language)) {
            return hljs.highlight(response.content, {language}).value;
        }
        return hljs.highlightAuto(response.content).value;
    }

}
