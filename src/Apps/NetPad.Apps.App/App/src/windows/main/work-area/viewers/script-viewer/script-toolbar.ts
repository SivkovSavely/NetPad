import {bindable, ILogger} from "aurelia";
import {IHttpClient} from "@aurelia/fetch-client";
import {
    DataConnection,
    DataConnectionStore,
    DotNetFrameworkVersion,
    IAppService,
    IEventBus,
    IScriptService,
    IShortcutManager,
    OptimizationLevel,
    Script,
    ScriptEnvironment,
    ScriptKind,
    EnvironmentPropertyChangedEvent,
    ViewModelBase
} from "@application";
import {ViewableScriptDocument} from "./viewable-script-document";

export class ScriptToolbar extends ViewModelBase {
    @bindable viewable: ViewableScriptDocument;
    public availableFrameworkVersions: DotNetFrameworkVersion[] = [];
    private readonly baseLogger: ILogger;
    public hostCpuPercent?: number;
    public hostWorkingSetBytes?: number;
    private polling = false;
    private attachedToDom = false;
    private pollingGeneration = 0;

    constructor(@IScriptService private readonly scriptService: IScriptService,
                @IAppService private readonly appService: IAppService,
                @IShortcutManager private readonly shortcutManager: IShortcutManager,
                @IEventBus private readonly eventBus: IEventBus,
                 private readonly dataConnectionStore: DataConnectionStore,
                 @IHttpClient private readonly http: IHttpClient,
                 @ILogger logger: ILogger) {
        super(logger);
        this.baseLogger = this.logger;
    }

    public get environment(): ScriptEnvironment | null | undefined {
        return this.viewable?.environment;
    }

    public get isBusy(): boolean {
        const status = this.environment?.status;
        return status === "Running" || status === "Stopping";
    }

    public get script(): Script | null | undefined {
        return this.environment?.script;
    }

    public get kind(): ScriptKind | null | undefined {
        return this.script?.config.kind;
    }

    public set kind(value) {
        if (!this.script || !value || this.script.config.kind === value) return;

        this.logger.debug("Setting script kind to:", value);

        this.scriptService.setScriptKind(this.script.id, value)
            .catch(err => {
                this.logger.error("Failed to set script kind", err);
            });
    }

    public get targetFrameworkVersion(): DotNetFrameworkVersion | null | undefined {
        return this.script?.config.targetFrameworkVersion;
    }

    public set targetFrameworkVersion(value) {
        if (!this.script || !value || this.script.config.targetFrameworkVersion === value) return;

        this.logger.debug("Setting targetFrameworkVersion to:", value);

        this.scriptService.setTargetFrameworkVersion(this.script.id, value)
            .catch(err => {
                this.logger.error("Failed to set script targetFrameworkVersion", err);
            });
    }

    public get dataConnection(): DataConnection | undefined {
        const connection = this.script?.dataConnection;

        if (!connection)
            return undefined;

        // We want to return the connection object from the connection store, not the connection
        // defined in the script.dataConnection property because they both reference 2 different
        // object instances, even though they are "the same connection"
        return this.dataConnectionStore.connections.find(c => c.id == connection.id);
    }

    public set dataConnection(value: DataConnection | undefined) {
        if (!this.script || this.script.dataConnection?.id === value?.id) return;

        this.logger.debug("Setting data connection to:", value);

        this.scriptService.setDataConnection(this.script.id, value?.id)
            .catch(err => {
                this.logger.error("Failed to set script data connection", err);
            });
    }

    public get optimizationLevel(): OptimizationLevel | null | undefined {
        return this.script?.config.optimizationLevel;
    }

    public set optimizationLevel(value) {
        if (!this.script || !value || this.script.config.optimizationLevel === value) return;

        this.logger.debug("Setting optimizationLevel to:", value);

        this.scriptService.setOptimizationLevel(this.script.id, value)
            .catch(err => {
                this.logger.error("Failed to set script optimizationLevel", err);
            });
    }

    public attached() {
        this.attachedToDom = true;
        this.addDisposable(this.eventBus.subscribeToServer(EnvironmentPropertyChangedEvent, event => {
            if (event.scriptId !== this.script?.id || event.propertyName !== "IsScriptHostRunning") return;
            if (event.newValue && !this.polling) {
                this.polling = true;
                void this.pollHostResources(++this.pollingGeneration);
            } else if (!event.newValue) {
                this.polling = false;
                this.pollingGeneration++;
                this.hostCpuPercent = undefined;
                this.hostWorkingSetBytes = undefined;
            }
        }));
        this.appService.getAvailableDotNetSdkVersions().then(result => this.availableFrameworkVersions = result);
        if (this.environment?.isScriptHostRunning) {
            this.polling = true;
            void this.pollHostResources(++this.pollingGeneration);
        }
    }

    public override detaching() {
        this.attachedToDom = false;
        this.polling = false;
        this.pollingGeneration++;
    }

    private async pollHostResources(generation: number) {
        while (this.attachedToDom && this.polling && generation === this.pollingGeneration && this.environment?.isScriptHostRunning) {
            const environment = this.environment;
            if (environment?.isScriptHostRunning) {
                try {
                    const response = await this.http.fetch(`/session/environments/${environment.script.id}/resources`);
                    if (!this.attachedToDom || generation !== this.pollingGeneration || this.environment !== environment) return;
                    if (response.ok && response.status !== 204) {
                        const sample = await response.json();
                        this.hostCpuPercent = sample.cpuPercentOfOneCore;
                        this.hostWorkingSetBytes = sample.workingSetBytes;
                    } else {
                        this.hostCpuPercent = undefined;
                        this.hostWorkingSetBytes = undefined;
                    }
                } catch (error) {
                    this.logger.debug("Could not poll script-host resources", error);
                }
            }
            await new Promise(resolve => setTimeout(resolve, 2000));
        }
    }

    public formatWorkingSet(bytes?: number): string {
        if (bytes == null) return "";
        const units = ["B", "KB", "MB", "GB"];
        let value = bytes;
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
        return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
    }

    public async run() {
        await this.viewable?.run();
    }

    public async stop() {
        await this.viewable?.stop();
    }

    public async save() {
        await this.viewable?.save();
    }

    public getShortcutKeyCombo(shortcutName: string) {
        return this.shortcutManager.getShortcutByName(shortcutName)?.toString()
    }

    public async openProperties() {
        await this.viewable?.openProperties();
    }

    private viewableChanged() {
        this.polling = false;
        this.pollingGeneration++;
        this.hostCpuPercent = undefined;
        this.hostWorkingSetBytes = undefined;
        this.logger = !this.script
            ? this.baseLogger
            : this.baseLogger.scopeTo(`[${this.script?.id}] ${this.environment?.script.name}`);

        if (this.attachedToDom && this.environment?.isScriptHostRunning) {
            this.polling = true;
            void this.pollHostResources(++this.pollingGeneration);
        }
    }
}
