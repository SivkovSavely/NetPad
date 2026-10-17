import {DI, IContainer} from "aurelia";
import {WithDisposables} from "@common";
import {CreateScriptDto, EnvironmentsRemovedEvent, ScriptEnvironment} from "@application/api";
import {IAppService} from "@application/app/iapp-service";
import {IEventBus} from "@application/events/ievent-bus";
import {IScriptService} from "@application/scripts/iscript-service";
import {ISession} from "@application/sessions/isession";
import {RunScriptCommand} from "@application/scripts/run-script-command";
import {ViewerHostCollection} from "./viewers/viewer-host-collection";
import {ViewerHost} from "./viewers/viewer-host";
import {ViewableObject} from "./viewers/viewable-object";
import {ViewableScriptDocument} from "./viewers/script-viewer/viewable-script-document";
import {IWorkAreaAppearance} from "./work-area-appearance";
import {NativeTextFile, NativeTextFileReference} from "@application/dialogs/inative-text-file-service";
import {INativeTextFileService} from "@application/dialogs/inative-text-file-service";
import {ViewableFileDocument} from "./viewers/file-viewer/viewable-file-document";
import {LibraryTextFileService} from "@application/file-search/library-text-file-service";
import {IFileDocumentDialogService} from "./viewers/file-viewer/file-document-dialog-service";

export const IWorkAreaService = DI.createInterface<IWorkAreaService>();

export interface IWorkAreaService {
    readonly viewerHosts: ViewerHostCollection;
    readonly appearance: IWorkAreaAppearance;
    readonly activeViewable: ViewableObject | undefined;

    /**
     * Creates the initial viewer host, subscribes to workbench-level commands, and
     * ensures at least one script is open. Idempotent — safe to call multiple times.
     */
    initialize(): Promise<void>;

    findViewable(id: string): { viewable: ViewableObject, host: ViewerHost } | undefined;

    /**
     * Adds the viewable to a host (the supplied one, otherwise the active host,
     * otherwise the first host).
     */
    open(viewable: ViewableObject, targetHost?: ViewerHost): Promise<void>;

    /**
     * Factory for script viewables. Script-specific for now; when additional
     * viewable types are added, extract a separate IViewableFactory.
     */
    createScriptViewable(environment: ScriptEnvironment): ViewableScriptDocument;

    createFileViewable(file: NativeTextFile): ViewableFileDocument;

    openFile(file: NativeTextFile, lineNumber?: number): Promise<ViewableFileDocument>;

    readonly recentFiles: ReadonlyArray<NativeTextFileReference>;

    onRecentFilesChanged(callback: () => void): {dispose(): void};

    recordRecentFile(file: NativeTextFileReference): void;

    openRecentFile(handle: string): Promise<ViewableFileDocument>;

    removeRecentFile(handle: string): void;

    clearRecentFiles(): void;
}

export class WorkAreaService extends WithDisposables implements IWorkAreaService {
    public readonly viewerHosts = new ViewerHostCollection();
    private _initialized = false;
    private _recentFiles: NativeTextFileReference[] = [];
    private readonly recentFilesChanged = new Set<() => void>();

    constructor(
        @IContainer private readonly container: IContainer,
        @IWorkAreaAppearance public readonly appearance: IWorkAreaAppearance,
        @IScriptService private readonly scriptService: IScriptService,
        @ISession private readonly session: ISession,
        @IAppService private readonly appService: IAppService,
        @IEventBus private readonly eventBus: IEventBus,
    ) {
        super();
        this.appearance.load();
        this.addDisposable(this.appearance);
    }

    public get activeViewable(): ViewableObject | undefined {
        return this.viewerHosts.active?.activeViewable;
    }

    public async initialize(): Promise<void> {
        if (this._initialized) return;
        this._initialized = true;

        // Create the initial viewer host if none exist.
        if (this.viewerHosts.items.length === 0) {
            const viewHostFactory = this.container.getFactory(ViewerHost);
            this.viewerHosts.add(viewHostFactory.construct(this.container));
        }

        // Handle RunScriptCommand
        this.addDisposable(
            this.eventBus.subscribe(RunScriptCommand, async msg => {
                const target = msg.scriptId !== undefined
                    ? this.findViewable(msg.scriptId)?.viewable
                    : this.activeViewable;

                if (target?.canRun()) {
                    await target.run();
                }
            })
        );

        // Always keep at least one script open.
        this.addDisposable(
            this.eventBus.subscribeToServer(EnvironmentsRemovedEvent, () => {
                void this.ensureAtLeastOneScript();
            })
        );

        await this.ensureAtLeastOneScript();
    }

    private async ensureAtLeastOneScript(): Promise<void> {
        if (this.session.environments.length === 0) {
            await this.scriptService.create(new CreateScriptDto());
        }
    }

    public findViewable(id: string): { viewable: ViewableObject, host: ViewerHost } | undefined {
        return this.viewerHosts.findViewable(id);
    }

    public async open(viewable: ViewableObject, targetHost?: ViewerHost): Promise<void> {
        const host = targetHost ?? this.viewerHosts.active ?? this.viewerHosts.items[0];
        if (!host) {
            throw new Error("No viewer host available to open viewable");
        }
        await viewable.open(host);
    }

    public createScriptViewable(environment: ScriptEnvironment): ViewableScriptDocument {
        return new ViewableScriptDocument(
            environment,
            this.scriptService,
            this.session,
            this.appService,
            this,
            this.eventBus,
        );
    }

    public createFileViewable(file: NativeTextFile): ViewableFileDocument {
        const contentService = file.source === "library"
            ? this.container.get(LibraryTextFileService)
            : this.container.get(INativeTextFileService);
        const nativeService = file.source === "library"
            ? undefined
            : this.container.get(INativeTextFileService);
        return new ViewableFileDocument(
            file,
            contentService,
            nativeService,
            this.container.get(IFileDocumentDialogService),
            this,
        );
    }

    public async openFile(file: NativeTextFile, lineNumber?: number): Promise<ViewableFileDocument> {
        this.recordRecentFile(file);
        const id = ViewableFileDocument.idForPath(file.path);
        const existing = this.findViewable(id) ?? this.viewerHosts.items
            .flatMap(host => [...host.viewables].map(viewable => ({viewable, host})))
            .find(item => item.viewable instanceof ViewableFileDocument
                && ViewableFileDocument.idForPath(item.viewable.path ?? "") === id);
        if (existing?.viewable instanceof ViewableFileDocument) {
            existing.viewable.textDocument.pendingLine = lineNumber;
            existing.host.activate(existing.viewable);
            await this.viewerHosts.activate(existing.host);
            return existing.viewable;
        }

        const viewable = this.createFileViewable(file);
        viewable.textDocument.pendingLine = lineNumber;
        const host = this.viewerHosts.active ?? this.viewerHosts.items[0];
        await this.open(viewable, host);
        host?.activate(viewable);
        await this.viewerHosts.activate(host ?? null);
        return viewable;
    }

    public get recentFiles(): ReadonlyArray<NativeTextFileReference> {
        return this._recentFiles;
    }

    public onRecentFilesChanged(callback: () => void): {dispose(): void} {
        this.recentFilesChanged.add(callback);
        return {dispose: () => this.recentFilesChanged.delete(callback)};
    }

    public recordRecentFile(file: NativeTextFileReference): void {
        this._recentFiles = [
            file,
            ...this._recentFiles.filter(recent => recent.path !== file.path),
        ].slice(0, 15);
        for (const callback of this.recentFilesChanged) callback();
    }

    public async openRecentFile(handle: string): Promise<ViewableFileDocument> {
        const reference = this._recentFiles.find(file => file.handle === handle);
        if (!reference) throw new Error("Recent text file is no longer available");
        const service = reference.source === "library"
            ? this.container.get(LibraryTextFileService)
            : this.container.get(INativeTextFileService);
        const text = await service.readTextFile(reference.handle);
        return this.openFile({...reference, text});
    }

    public removeRecentFile(handle: string): void {
        this._recentFiles = this._recentFiles.filter(file => file.handle !== handle);
        for (const callback of this.recentFilesChanged) callback();
    }

    public clearRecentFiles(): void {
        this._recentFiles = [];
        for (const callback of this.recentFilesChanged) callback();
    }
}
