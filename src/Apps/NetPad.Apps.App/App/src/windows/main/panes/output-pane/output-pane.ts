import {PLATFORM} from "aurelia";
import {watch} from "@aurelia/runtime-html";
import {
    ChannelInfo,
    EnvironmentPropertyChangedEvent,
    IEventBus,
    IIpcGateway,
    IPaneManager,
    ISession,
    IShortcutManager,
    IScriptService,
    IWindowService,
    KeyCombo,
    Pane,
    PromptUserForInputCommand,
    ResultHostCommandEvent,
    RunJsInResultsCommand,
    ScriptEnvironment,
    ScriptHtmlHeadChangedEvent,
    ScriptOutputEmittedEvent,
    ScriptStatus,
    Settings,
    ShortcutIds
} from "@application";
import {CodePane} from "../code-pane/code-pane";
import {AppWindows} from "@application/windows/app-windows";
import {OutputModel} from "./output-model";
import {DisposableCollection, KeyCode} from "@common";
import {FindTextBox} from "@application/find-text-box/find-text-box";
import {DumpContainer} from "./components/dump-container";

export class OutputPane extends Pane {
    public outputModels = new Map<string, OutputModel>();
    private current?: OutputModel;
    private disposables = new DisposableCollection();
    private findTextBox: FindTextBox;
    private activeTab = "Results";
    private tabs = [
        {
            name: "Results",
            keyBinding: new KeyCombo().withAltKey().withKey(KeyCode.Digit1),
        },
        {
            name: "SQL",
            keyBinding: new KeyCombo().withAltKey().withKey(KeyCode.Digit2),
        },
    ]

    constructor(
        private readonly element: Element,
        @ISession public readonly session: ISession,
        @IWindowService private readonly windowService: IWindowService,
        @IEventBus private eventBus: IEventBus,
        @IShortcutManager shortcutManager: IShortcutManager,
        @IScriptService private readonly scriptService: IScriptService,
        @IIpcGateway private readonly ipcGateway: IIpcGateway,
        @IPaneManager private readonly paneManager: IPaneManager,
        private readonly appWindows: AppWindows,
        private readonly settings: Settings
    ) {
        super("Output", "output-icon", false);
        this.hasShortcut(shortcutManager.getShortcut(ShortcutIds.openOutput));
    }

    public bound() {
        this.setCurrentOutputModel(this.session.active);
    }

    public attached() {
        this.listenForScriptStatusChanges();
        this.listenForOutputMessages();
        this.listenForResultHostCommands();
        this.listenForHtmlHeadChanges();

        if (!this.isWindow) {
            this.listenForExternalOutputWindowMessages();
        }

        const tabKeysHandler = (ev: Event) => {
            const match = this.tabs.find(t => t.keyBinding.matches(ev as KeyboardEvent));
            if (match) {
                this.activeTab = match.name;
            }
        };

        this.element.addEventListener("keydown", tabKeysHandler);
        this.disposables.add(() => this.element.removeEventListener("keydown", tabKeysHandler));

        PLATFORM.queueMicrotask(() => this.activatePaneIfApplicable());
    }

    private listenForScriptStatusChanges() {
        this.disposables.add(
            this.eventBus.subscribeToServer(EnvironmentPropertyChangedEvent, msg => {
                if (msg.propertyName == "Status") {
                    const model = this.outputModels.get(msg.scriptId);
                    if (model) {
                        const status = msg.newValue as ScriptStatus;

                        if (status == "Running") {
                            model.inputRequest = null;
                            model.resultsDumpContainer.clearOutput(true);
                            model.sqlDumpContainer.clearOutput(true);
                            model.clearPanels();
                        }
                        else {
                            model.inputRequest = null;
                        }
                    }
                }
            })
        );
    }

    private listenForOutputMessages() {
        this.disposables.add(
            this.eventBus.subscribeToServer(ScriptOutputEmittedEvent, msg => {
                if (!msg.output) {
                    return;
                }

                const environment = this.session.environments.find(e => e.script.id == msg.scriptId)
                    ?? this.outputModels.get(msg.scriptId)?.environment;

                if (!environment) {
                    this.logger.warn(`Got output for script ${msg.scriptId} but no model found for it. Message: `, msg);
                    return;
                }

                const model = this.getOrCreateModel(environment);

                // Named result panel outputs are routed to their own containers.
                if (msg.output.panelName) {
                    if (msg.output.kind === "Result" || msg.output.kind === "Error") {
                        const container = model.getOrCreatePanel(msg.output.panelName);
                        container.appendOutput(msg.output);

                        if (this.current === model && this.activeTab !== msg.output.panelName && this.activeTab !== "SQL") {
                            this.activeTab = msg.output.panelName;
                        }
                    }
                    return;
                }

                if (msg.output.kind === "Result" || msg.output.kind === "Error") {
                    model.resultsDumpContainer.appendOutput(msg.output);
                } else if (msg.output.kind === "Sql") {
                    model.sqlDumpContainer.appendOutput(msg.output);
                } else {
                    this.logger.warn(`Got output for script ${msg.scriptId} but message type ${msg.output.kind} is unhandled. Message: `, msg);
                }
            })
        );

        this.disposables.add(
            this.eventBus.subscribeToServer(PromptUserForInputCommand, msg => {
                const model = this.outputModels.get(msg.scriptId);
                if (!model) {
                    this.logger.warn(`Got user input command for script ${msg.scriptId} but no model found for it. Message: `, msg);
                    return;
                }

                model.inputRequest = {
                    commandId: msg.requestId,
                    masked: msg.isMasked === true
                };

                setTimeout(() => {
                    (this.element.querySelector(".user-input-container input") as HTMLInputElement)?.focus();
                }, 50);
            })
        );

        // Util.JS requests are evaluated in this window's context (the results view lives here).
        this.disposables.add(
            this.eventBus.subscribeToServer(RunJsInResultsCommand, msg => {
                void this.runJsInResultsView(msg);
            })
        );
    }

    private listenForResultHostCommands() {
        this.disposables.add(
            this.eventBus.subscribeToServer(ResultHostCommandEvent, msg => {
                this.handleResultHostCommand(msg);
            })
        );
    }

    private listenForHtmlHeadChanges() {
        if (this.isWindow) return; // Head customizations live in the main window's document.

        this.disposables.add(
            this.eventBus.subscribeToServer(ScriptHtmlHeadChangedEvent, msg => {
                this.applyHtmlHeadEntries(msg.scriptId, msg.entries ?? []);
            })
        );
    }

    private getOrCreateModel(environment: ScriptEnvironment): OutputModel {
        let model = this.outputModels.get(environment.script.id);

        if (!model) {
            model = this.createModel(environment);
        }

        return model;
    }

    private createModel(environment: ScriptEnvironment): OutputModel {
        let model = this.outputModels.get(environment.script.id);

        if (model) return model;

        model = new OutputModel(environment, this.settings);
        this.wireContainerCallbacks(model.environment.script.id, model.resultsDumpContainer);
        this.outputModels.set(environment.script.id, model);
        return model;
    }

    private wireContainerCallbacks(scriptId: string, container: DumpContainer) {
        container.onExpandOnDemand =
            outputId => void this.scriptService.expandOnDemand(scriptId, outputId);
        container.onNavigateToSource =
            (path, line) => void this.session.openByPath(path).catch(() => undefined);
        container.onInvokeAction =
            actionId => void this.scriptService.invokeScriptAction(scriptId, actionId);
    }

    private handleResultHostCommand(msg: ResultHostCommandEvent) {
        const environment = this.session.environments.find(e => e.script.id == msg.scriptId)
            ?? this.outputModels.get(msg.scriptId)?.environment;

        if (!environment) return;

        const model = this.getOrCreateModel(environment);
        const isActive = this.current === model;

        switch (msg.command) {
            case "ClearResults":
                model.resultsDumpContainer.clearOutput(true);
                break;

            case "HideEditor":
                if (!this.isWindow) this.paneManager.collapse(CodePane);
                break;

            case "ShowEditor":
                if (!this.isWindow) this.paneManager.expand(CodePane);
                break;

            case "HideResults":
                this.hide();
                break;

            case "ShowResults":
                this.activate();
                break;

            case "AutoScrollResults": {
                const enabled = msg.payloadJson === "true";
                model.resultsDumpContainer.scrollOnOutput = enabled;
                for (const panel of model.panels.values()) {
                    panel.scrollOnOutput = enabled;
                }
                break;
            }

            case "OpenPanel": {
                const name = parsePanelPayload(msg.payloadJson);
                if (!name) break;

                const container = model.getOrCreatePanel(name);
                this.wireContainerCallbacks(environment.script.id, container);

                if (isActive) this.activeTab = name;
                break;
            }

            case "RemovePanel": {
                const name = parsePanelPayload(msg.payloadJson);
                if (!name) break;

                model.removePanel(name);

                if (isActive && this.activeTab === name) {
                    this.activeTab = model.panelNames.length > 0
                        ? model.panelNames[model.panelNames.length - 1]
                        : "Results";
                }
                break;
            }
        }
    }

    private applyHtmlHeadEntries(scriptId: string, entries: {type: number; content: string}[]) {
        const head = document.head;

        head.querySelectorAll(`[data-netpad-script-head="${scriptId}"]`).forEach(el => el.remove());

        for (const entry of entries) {
            const el = this.createHtmlHeadElement(entry);
            if (!el) continue;

            el.setAttribute("data-netpad-script-head", scriptId);
            head.appendChild(el);
        }
    }

    private createHtmlHeadElement(entry: {type: number; content: string}): HTMLElement | null {
        switch (entry.type) {
            case 1: { // Css
                const style = document.createElement("style");
                style.textContent = entry.content;
                return style;
            }
            case 2: { // CssLink
                const link = document.createElement("link");
                link.rel = "stylesheet";
                link.href = entry.content;
                return link;
            }
            case 3: { // ScriptLink
                const script = document.createElement("script");
                script.src = entry.content;
                return script;
            }
            case 4: { // Script
                const script = document.createElement("script");
                script.textContent = entry.content;
                return script;
            }
            case 5: { // Raw
                const template = document.createElement("template");
                template.innerHTML = entry.content;
                const first = template.content.firstElementChild;
                if (first && template.content.children.length === 1) {
                    return first.cloneNode(true) as HTMLElement;
                }
                const span = document.createElement("span");
                span.appendChild(template.content);
                return span;
            }
            default:
                return null;
        }
    }

    private async runJsInResultsView(msg: RunJsInResultsCommand) {
        let response: {resultJson: string | null; error: string | null};

        if (this.isWindow) {
            response = {resultJson: null, error: "JavaScript evaluation is only available in the main window."};
        } else {
            try {
                // Indirect eval executes in global scope of this window, which hosts the script's results view.
                const result = (0, eval)(msg.code);
                response = {resultJson: safeJsonStringify(result), error: null};
            } catch (err: any) {
                response = {resultJson: null, error: err?.message ?? String(err)};
            }
        }

        await this.ipcGateway.send(new ChannelInfo("Respond"), msg.requestId, response);
    }

    @watch<OutputPane>(vm => vm.session.active)
    private setCurrentOutputModel(active?: ScriptEnvironment | null) {
        let newCurrent: OutputModel | undefined = undefined;

        if (active) {
            let model = this.outputModels.get(active.script.id);

            if (!model) {
                model = this.createModel(active);
            }

            newCurrent = model;
        }

        this.current = newCurrent;

        if (this.current) {
            this.findTextBox.registerSearchableElement(
                this.current.resultsDumpContainer.element,
                ".null, .property-value, .property-name, .text, .group > .title");

            this.findTextBox.registerSearchableElement(
                this.current.sqlDumpContainer.element,
                ".text, .sql-keyword, .query-time, .query-params, .logger-name, .not-special");

            this.setFindTextBoxSearchableElement();
        }
    }

    @watch<OutputPane>(vm => vm.session.environments.length)
    private destroyUnneededOutputModels() {
        const environments = this.session.environments;

        const removed = [...this.outputModels.keys()]
            .filter(id => !environments.some(e => e.script.id === id));

        for (const id of removed) {
            const model = this.outputModels.get(id);

            if (model) {
                this.findTextBox.unregisterSearchableElement(model.resultsDumpContainer.element);
                this.findTextBox.unregisterSearchableElement(model.sqlDumpContainer.element);
                this.applyHtmlHeadEntries(id, []);

                model.destroy();
                this.outputModels.delete(id);
            }
        }
    }

    @watch<OutputPane>(vm => vm.activeTab)
    private setFindTextBoxSearchableElement() {
        PLATFORM.queueMicrotask(() => {
            if (!this.current) {
                return;
            }

            if (this.activeTab === 'Results') {
                this.findTextBox.setCurrent(this.current.resultsDumpContainer.element);
            } else {
                const panelContainer = this.current.panels.get(this.activeTab);
                this.findTextBox.setCurrent(panelContainer?.element ?? this.current.sqlDumpContainer.element);
            }
        });
    }

    @watch<OutputPane>(vm => vm.session.active?.status)
    private activatePaneIfApplicable() {
        if (this.appWindows.items.find(x => x.name === "output")) {
            return;
        }

        if (this.settings.results.openOnRun && this.session.active?.status === "Running") {
            this.activate();
        }
    }

    private async openExternalOutputWindow() {
        this.hide();
        await this.windowService.openOutputWindow();
    }

    @watch<OutputPane>(vm => vm.appWindows.items.map(x => x.name))
    private reactToExternalWindowState(currentWindowNames: string, previousWindowNames: string) {
        if (this.isWindow) {
            return;
        }

        const wasOpen = previousWindowNames.indexOf("output") >= 0;
        const currentlyOpen = currentWindowNames.indexOf("output") >= 0;
        const hostHasAnActivePaneOpen = this.host?.active;

        if (wasOpen && !currentlyOpen && !hostHasAnActivePaneOpen) {
            this.activate();
        } else if (currentlyOpen) {
            this.hide();
        }
    }

    private listenForExternalOutputWindowMessages() {
        // External window will request current outputs
        const bc = new BroadcastChannel("output-window");

        bc.onmessage = (ev) => {
            if (ev.data !== "send-outputs") {
                return;
            }

            bc.postMessage([...this.outputModels.values()].map(m => m.toDto()));
        };

        this.disposables.add(() => bc.close());
    }
}

function parsePanelPayload(payloadJson?: string | null): string | null {
    if (!payloadJson) return null;

    try {
        const parsed = JSON.parse(payloadJson);
        return typeof parsed === "string" ? parsed : null;
    } catch {
        return null;
    }
}

function safeJsonStringify(value: any): string | null {
    try {
        return JSON.stringify(value) ?? null;
    } catch {
        return JSON.stringify(String(value));
    }
}
