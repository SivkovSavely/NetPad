import {DI, IContainer} from "aurelia";
import {Util, WithDisposables} from "@common";
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
import {ViewableTextDocument} from "./viewers/viewable-text-document";
import {LibraryTextFileService} from "@application/file-search/library-text-file-service";
import {IFileDocumentDialogService} from "./viewers/file-viewer/file-document-dialog-service";
import {ITextEditor} from "@application/editor/text-editor";
import {getBookmarkManager} from "@application/editor/bookmark-manager";
import {
    IWorkspaceService,
    WorkspaceDocumentState,
    WorkspaceManifest,
    WorkspaceShelf,
    WorkspaceState,
} from "@application/workspaces/workspace-service";

export const IWorkAreaService = DI.createInterface<IWorkAreaService>();

export interface IWorkAreaService {
    readonly viewerHosts: ViewerHostCollection;
    readonly appearance: IWorkAreaAppearance;
    readonly activeViewable: ViewableObject | undefined;
    readonly workspaceManifest: WorkspaceManifest;
    readonly currentWorkspace: WorkspaceState;
    readonly isRestoringWorkspace: boolean;

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

    restoreWorkspace(): Promise<void>;
    getViewablesOrder(hostName: string): string[];
    setViewablesOrder(hostName: string, ids: string[], migrated?: boolean): void;
    createWorkspace(name: string): Promise<void>;
    renameWorkspace(name: string): Promise<void>;
    switchWorkspace(workspaceId: string): Promise<void>;
    deleteWorkspace(): Promise<void>;
    createShelf(name: string): Promise<void>;
    restoreShelf(shelfId: string): Promise<void>;
    deleteShelf(shelfId: string): Promise<void>;
}

export class WorkAreaService extends WithDisposables implements IWorkAreaService {
    public readonly viewerHosts = new ViewerHostCollection();
    private _initialized = false;
    private _recentFiles: NativeTextFileReference[] = [];
    private readonly recentFilesChanged = new Set<() => void>();
    private readonly tabOrders = new Map<string, string[]>();
    private readonly pendingLegacyOrderKeys = new Set<string>();
    private workspaceReady = false;
    private restoringWorkspace = false;
    private firstWorkspaceRestore = true;
    private saveTimer?: ReturnType<typeof setTimeout>;
    private workspaceSaveQueue: Promise<void> = Promise.resolve();
    private readonly pageHideHandler = () => {
        void this.persistWorkspace(true).catch(error => console.error("Could not flush workspace state", error));
    };

    constructor(
        @IContainer private readonly container: IContainer,
        @IWorkAreaAppearance public readonly appearance: IWorkAreaAppearance,
        @IScriptService private readonly scriptService: IScriptService,
        @ISession private readonly session: ISession,
        @IAppService private readonly appService: IAppService,
        @IEventBus private readonly eventBus: IEventBus,
        @IWorkspaceService private readonly workspaceService: IWorkspaceService,
    ) {
        super();
        this.appearance.load();
        this.addDisposable(this.appearance);
    }

    public get activeViewable(): ViewableObject | undefined {
        return this.viewerHosts.active?.activeViewable;
    }

    public get workspaceManifest(): WorkspaceManifest {
        return this.workspaceService.manifest;
    }

    public get currentWorkspace(): WorkspaceState {
        return this.workspaceManifest.workspaces.find(x => x.id === this.workspaceManifest.activeWorkspaceId)
            ?? this.workspaceManifest.workspaces[0]
            ?? {id: "default", name: "Default", documents: [], activeHostName: "main"};
    }

    public get isRestoringWorkspace(): boolean {
        return this.restoringWorkspace;
    }

    public async initialize(): Promise<void> {
        if (this._initialized) return;
        this._initialized = true;

        // Create the initial viewer host if none exist.
        if (this.viewerHosts.items.length === 0) {
            const viewHostFactory = this.container.getFactory(ViewerHost);
            this.viewerHosts.add(viewHostFactory.construct(this.container));
        }

        await this.workspaceService.initialize();
        this.loadLegacyTabOrders();
        this.getOrCreateHost(this.currentWorkspace.activeHostName);
        for (const document of this.currentWorkspace.documents) {
            this.getOrCreateHost(document.hostName);
        }
        this.addDisposable(this.viewerHosts.onChanged(() => this.scheduleWorkspaceSave()));
        window.addEventListener("pagehide", this.pageHideHandler);
        this.addDisposable(() => window.removeEventListener("pagehide", this.pageHideHandler));
        this.addDisposable(() => this.saveTimer && clearTimeout(this.saveTimer));

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
        this.scheduleWorkspaceSave();
    }

    public createScriptViewable(environment: ScriptEnvironment): ViewableScriptDocument {
        const viewable = new ViewableScriptDocument(
            environment,
            this.scriptService,
            this.session,
            this.appService,
            this,
            this.container.get(IFileDocumentDialogService),
            this.eventBus,
        );
        viewable.addDisposable(viewable.textDocument.onChange(async () => this.scheduleWorkspaceSave()));
        return viewable;
    }

    public createFileViewable(file: NativeTextFile, options?: {id?: string; name?: string; path?: string; savedText?: string}): ViewableFileDocument {
        const contentService = file.source === "library"
            ? this.container.get(LibraryTextFileService)
            : this.container.get(INativeTextFileService);
        const nativeService = file.source === "library"
            ? undefined
            : this.container.get(INativeTextFileService);
        const viewable = new ViewableFileDocument(
            file,
            contentService,
            nativeService,
            this.container.get(IFileDocumentDialogService),
            this,
            options,
        );
        viewable.addDisposable(viewable.textDocument.onChange(async () => this.scheduleWorkspaceSave()));
        return viewable;
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
        this.persistRecentFilePaths();
        for (const callback of this.recentFilesChanged) callback();
    }

    public async openRecentFile(handle: string): Promise<ViewableFileDocument> {
        const reference = this._recentFiles.find(file => file.handle === handle);
        if (!reference) throw new Error("Recent text file is no longer available");

        if (reference.handle.startsWith("workspace-recent:")) {
            const file = reference.source === "library"
                ? await this.container.get(LibraryTextFileService).openTextFile(reference.path)
                : await this.container.get(INativeTextFileService).reopenTextFile?.(reference.path);
            if (!file) throw new Error("This shell cannot reopen local files by path");
            return this.openFile(file);
        }

        const service = reference.source === "library"
            ? this.container.get(LibraryTextFileService)
            : this.container.get(INativeTextFileService);
        const text = await service.readTextFile(reference.handle);
        return this.openFile({...reference, text});
    }

    public removeRecentFile(handle: string): void {
        this._recentFiles = this._recentFiles.filter(file => file.handle !== handle);
        this.persistRecentFilePaths();
        for (const callback of this.recentFilesChanged) callback();
    }

    public clearRecentFiles(): void {
        this._recentFiles = [];
        this.persistRecentFilePaths();
        for (const callback of this.recentFilesChanged) callback();
    }

    public getViewablesOrder(hostName: string): string[] {
        const inMemory = this.tabOrders.get(hostName);
        if (inMemory) return [...inMemory];
        return this.currentWorkspace.documents
            .filter(document => document.hostName === hostName)
            .sort((a, b) => a.order - b.order)
            .map(document => document.id);
    }

    public setViewablesOrder(hostName: string, ids: string[], migrated = false): void {
        const uniqueIds = [...new Set(ids)];
        this.tabOrders.set(hostName, uniqueIds);
        if (migrated) this.pendingLegacyOrderKeys.add(`tab-bar.${hostName}.viewables-order`);
        this.scheduleWorkspaceSave();
    }

    public async restoreWorkspace(): Promise<void> {
        if (this.restoringWorkspace) return;
        this.restoringWorkspace = true;
        try {
            this.loadRecentFilePaths();
            for (const host of this.viewerHosts.items) {
                const oldViewables = [...host.viewables];
                host.removeViewables(...oldViewables);
                for (const viewable of oldViewables) viewable.dispose();
            }

            const workspace = this.currentWorkspace;
            if (this.workspaceManifest.isInitialized) {
                this.tabOrders.clear();
                for (const hostName of new Set(workspace.documents.map(document => document.hostName))) {
                    this.tabOrders.set(hostName, workspace.documents
                        .filter(document => document.hostName === hostName)
                        .sort((a, b) => a.order - b.order)
                        .map(document => document.id));
                }
            }

            const restoreSavedWorkingSet = this.workspaceManifest.isInitialized && workspace.documents.length > 0;
            const restoredIds = new Set<string>();
            if (restoreSavedWorkingSet) {
                const documents = [...workspace.documents].sort((a, b) => {
                    const hostOrder = this.hostIndex(a.hostName) - this.hostIndex(b.hostName);
                    return hostOrder || a.order - b.order;
                });

                for (const state of documents) {
                    try {
                        const host = this.getOrCreateHost(state.hostName);
                        if (state.kind === "script") {
                            const environment = this.session.environments.find(env => env.script.id === state.id)
                                ?? await this.session.openById(state.id);

                            if (state.isDirty && state.buffer !== undefined && environment.script.code !== state.buffer) {
                                await this.scriptService.updateCode(state.id, state.buffer, false);
                            }
                            const viewable = this.createScriptViewable(environment);
                            await this.open(viewable, host);
                            await this.restoreDocumentState(viewable, host, state);
                            restoredIds.add(state.id);
                        } else {
                            const viewable = await this.restoreFileDocument(state, host);
                            if (viewable) restoredIds.add(state.id);
                        }
                    } catch (error) {
                        console.error(`Could not restore workspace document '${state.path || state.id}'`, error);
                        await this.safelyReportMissingDocument(state, String(error));
                    }
                }
            }

            // First-run migration still restores the legacy script session. At later
            // workspace switches only documents explicitly in that workspace are shown.
            if (!this.workspaceManifest.isInitialized || this.firstWorkspaceRestore) {
                const host = this.getOrCreateHost("main");
                for (const environment of this.session.environments) {
                    if (restoredIds.has(environment.script.id)) continue;
                    const viewable = this.createScriptViewable(environment);
                    await this.open(viewable, host);
                    restoredIds.add(environment.script.id);
                }
            }

            const activeHost = this.viewerHosts.items[this.hostIndex(workspace.activeHostName)];
            const activeInHost = workspace.activeDocumentId ? activeHost?.find(workspace.activeDocumentId) : undefined;
            const active = activeInHost && activeHost
                ? {viewable: activeInHost, host: activeHost}
                : workspace.activeDocumentId ? this.findViewable(workspace.activeDocumentId) : undefined;
            if (active) {
                active.host.activate(active.viewable);
                await this.viewerHosts.activate(active.host);
            } else if (this.session.active) {
                const activeScript = this.findViewable(this.session.active.script.id);
                if (activeScript) {
                    activeScript.host.activate(activeScript.viewable);
                    await this.viewerHosts.activate(activeScript.host);
                }
            }

            for (const host of this.viewerHosts.items) {
                if (!host.activeViewable && host.viewables.size > 0) {
                    host.activate(host.viewables.values().next().value!);
                }
            }
            if (!this.viewerHosts.active?.activeViewable) {
                const activeHost = this.viewerHosts.items[this.hostIndex(workspace.activeHostName)]
                    ?? this.viewerHosts.items.find(host => host.viewables.size > 0)
                    ?? this.viewerHosts.items[0];
                if (activeHost) await this.viewerHosts.activate(activeHost);
            }

            this.firstWorkspaceRestore = false;
            workspace.activeHostName = this.viewerHosts.active?.name ?? workspace.activeHostName ?? "main";
            document.title = this.activeViewable ? `${this.activeViewable.name} - NetPad` : "NetPad";
        } finally {
            this.restoringWorkspace = false;
            this.workspaceReady = true;
        }

        this.scheduleWorkspaceSave();
    }

    public async createWorkspace(name: string): Promise<void> {
        const normalized = name.trim();
        if (!normalized) return;
        await this.persistWorkspace();
        const workspace: WorkspaceState = {
            id: Util.newGuid(),
            name: normalized,
            documents: [],
            recentFiles: [],
            activeHostName: "main",
        };
        this.workspaceManifest.workspaces.push(workspace);
        this.workspaceManifest.activeWorkspaceId = workspace.id;
        await this.restoreWorkspace();
        await this.persistWorkspace();
    }

    public async renameWorkspace(name: string): Promise<void> {
        const normalized = name.trim();
        if (!normalized) return;
        this.currentWorkspace.name = normalized;
        await this.persistWorkspace();
    }

    public async switchWorkspace(workspaceId: string): Promise<void> {
        if (workspaceId === this.workspaceManifest.activeWorkspaceId) return;
        if (!this.workspaceManifest.workspaces.some(workspace => workspace.id === workspaceId)) return;
        await this.persistWorkspace();
        this.workspaceManifest.activeWorkspaceId = workspaceId;
        await this.restoreWorkspace();
        await this.persistWorkspace();
    }

    public async deleteWorkspace(): Promise<void> {
        const workspaceId = this.currentWorkspace.id;
        if (workspaceId === "default" || this.workspaceManifest.workspaces.length <= 1) return;
        this.workspaceManifest.workspaces = this.workspaceManifest.workspaces.filter(workspace => workspace.id !== workspaceId);
        this.workspaceManifest.activeWorkspaceId = "default";
        await this.restoreWorkspace();
        await this.persistWorkspace();
    }

    public async createShelf(name: string): Promise<void> {
        const normalized = name.trim();
        if (!normalized) return;
        await this.persistWorkspace();
        const snapshot = this.currentWorkspace;
        const documents = snapshot.documents.filter(document => document.isDirty);
        if (documents.length === 0) return;
        const shelf: WorkspaceShelf = {
            id: Util.newGuid(),
            name: normalized,
            createdUtc: new Date().toISOString(),
            state: {...snapshot, documents: documents.map(document => ({...document}))},
        };
        this.workspaceManifest.shelves.push(shelf);
        await this.workspaceService.save();
    }

    public async restoreShelf(shelfId: string): Promise<void> {
        const shelf = this.workspaceManifest.shelves.find(item => item.id === shelfId);
        if (!shelf) return;
        await this.persistWorkspace();
        const documents = new Map(this.currentWorkspace.documents.map(document => [document.id, document]));
        for (const document of shelf.state.documents) documents.set(document.id, {...document});
        this.currentWorkspace.documents = [...documents.values()];
        await this.restoreWorkspace();
        await this.persistWorkspace();
    }

    public async deleteShelf(shelfId: string): Promise<void> {
        this.workspaceManifest.shelves = this.workspaceManifest.shelves.filter(shelf => shelf.id !== shelfId);
        await this.workspaceService.save();
    }

    private getOrCreateHost(hostName: string): ViewerHost {
        const index = this.hostIndex(hostName);
        while (this.viewerHosts.items.length <= index) {
            const factory = this.container.getFactory(ViewerHost);
            this.viewerHosts.add(factory.construct(this.container));
        }
        return this.viewerHosts.items[index];
    }

    private hostIndex(hostName: string): number {
        if (hostName === "main") return 0;
        const match = /^split-(\d+)$/.exec(hostName);
        const index = match ? Number(match[1]) : 0;
        return Number.isSafeInteger(index) && index >= 0 && index <= 32 ? index : 0;
    }

    private async restoreFileDocument(state: WorkspaceDocumentState, host: ViewerHost): Promise<ViewableFileDocument | undefined> {
        let file: NativeTextFile;
        try {
            if (!state.path) {
                file = {
                    handle: "",
                    path: state.name || "Recovered File",
                    text: state.buffer ?? "",
                    source: state.source === "library" ? "library" : undefined,
                };
            } else if (state.source === "library") {
                file = await this.container.get(LibraryTextFileService).openTextFile(state.path);
            } else {
                const nativeFileService = this.container.get(INativeTextFileService);
                if (!nativeFileService.reopenTextFile) {
                    throw new Error("This shell cannot reopen local files by path");
                }
                file = await nativeFileService.reopenTextFile(state.path);
            }
        } catch (error) {
            await this.safelyReportMissingDocument(state, String(error));
            return undefined;
        }

        const isMemoryCopy = !state.path;
        if (!isMemoryCopy && !this._recentFiles.some(recent => recent.path === file.path)) {
            this.recordRecentFile(file);
        }
        const viewable = this.createFileViewable(file, isMemoryCopy
            ? {id: state.id, name: state.name || "Recovered File", path: "", savedText: ""}
            : undefined);
        let restoreBuffer = state.isDirty && state.buffer !== undefined;
        let restoreHistory = true;
        const diskChanged = !!state.diskFingerprint && !isMemoryCopy
            && await fingerprint(file.text) !== state.diskFingerprint;

        if (restoreBuffer && diskChanged) {
            const result = await this.container.get(IFileDocumentDialogService).ask({
                title: "File Changed While Closed",
                message: `'${state.path}' changed on disk while the workspace was closed. Choose how to restore the unsaved buffer.`,
                buttons: [
                    {text: "Keep Buffer", value: "keep", isPrimary: true},
                    {text: "Take Disk", value: "disk"},
                    {text: "Split Copy", value: "split"},
                    {text: "Cancel", value: "cancel"},
                ],
            });

            if (result?.value === "disk") {
                restoreBuffer = false;
                restoreHistory = false;
            } else if (result?.value === "split") {
                await this.open(viewable, host);
                await this.restoreDocumentState(viewable, host, {
                    ...state,
                    isDirty: false,
                    buffer: undefined,
                    bookmarks: [],
                    numberedBookmarks: {},
                    undoHistory: [],
                    redoHistory: [],
                });
                const copy = this.createFileViewable(
                    {...file, handle: "", text: state.buffer!},
                    {
                        id: Util.newGuid(),
                        name: `Recovered copy of ${state.name || ViewableFileDocument.getName(state.path!)}`,
                        path: "",
                        savedText: file.text,
                    },
                );
                await this.open(copy, host);
                await this.restoreDocumentState(copy, host, {...state, id: copy.id, name: copy.name});
                return viewable;
            }
        } else if (!restoreBuffer && diskChanged) {
            restoreHistory = false;
        }

        if (restoreBuffer) await viewable.textDocument.setText("server", state.buffer!);
        if (!restoreHistory) viewable.textDocument.resetHistory();
        await this.open(viewable, host);
        await this.restoreDocumentState(viewable, host, restoreHistory
            ? state
            : {...state, isDirty: false, buffer: undefined, undoHistory: [], redoHistory: []});
        return viewable;
    }

    private async restoreDocumentState(
        viewable: ViewableObject,
        host: ViewerHost,
        state: WorkspaceDocumentState,
    ): Promise<void> {
        if (!(viewable instanceof ViewableTextDocument)) return;
        const document = viewable.textDocument;
        document.restoreHistoryState({undo: state.undoHistory, redo: state.redoHistory});
        document.viewState = state.viewState;
        getBookmarkManager(viewable.id).restoreState({
            bookmarks: state.bookmarks,
            numberedBookmarks: state.numberedBookmarks,
        });

        const viewer = host.viewers.find(item => item.canOpen(viewable)) as {editor?: ITextEditor} | undefined;
        viewer?.editor?.restoreViewState(document.id, state.viewState);
    }

    private async reportMissingDocument(state: WorkspaceDocumentState, error?: string): Promise<void> {
        const label = state.path || state.name || state.id;
        const result = await this.container.get(IFileDocumentDialogService).ask({
            title: "Workspace Document Unavailable",
            message: `Could not restore '${label}'.${error ? ` ${error}` : ""}`,
            buttons: [
                {text: "Remove from Workspace", value: "remove", isPrimary: true},
                {text: "Keep for Now", value: "keep"},
            ],
        });
        if (result?.value === "remove") {
            this.currentWorkspace.documents = this.currentWorkspace.documents.filter(document => document.id !== state.id);
        }
    }

    private async safelyReportMissingDocument(state: WorkspaceDocumentState, error?: string): Promise<void> {
        try {
            await this.reportMissingDocument(state, error);
        } catch (dialogError) {
            console.error("Could not show missing workspace document dialog", dialogError);
        }
    }

    private scheduleWorkspaceSave(): void {
        if (!this.workspaceReady || this.restoringWorkspace) return;
        if (this.saveTimer) clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => {
            this.saveTimer = undefined;
            void this.persistWorkspace().catch(error => console.error("Could not save workspace state", error));
        }, 500);
    }

    private persistWorkspace(keepalive = false): Promise<void> {
        if (!this.workspaceReady || this.restoringWorkspace) return Promise.resolve();
        const request = this.workspaceSaveQueue.catch(() => undefined)
            .then(() => this.writeWorkspaceSnapshot(keepalive));
        this.workspaceSaveQueue = request;
        return request;
    }

    private async writeWorkspaceSnapshot(keepalive: boolean): Promise<void> {
        if (!this.workspaceReady || this.restoringWorkspace) return;
        const workspace = this.currentWorkspace;
        workspace.documents = await this.captureWorkspaceDocuments();
        workspace.activeDocumentId = this.activeViewable?.id;
        workspace.activeHostName = this.viewerHosts.active?.name ?? "main";
        this.updateAutomaticShelf(workspace);

        await this.workspaceService.save(keepalive);
        for (const key of this.pendingLegacyOrderKeys) {
            try { localStorage.removeItem(key); }
            catch (error) { console.error("Could not remove migrated tab order", error); }
        }
        this.pendingLegacyOrderKeys.clear();
    }

    private async captureWorkspaceDocuments(): Promise<WorkspaceDocumentState[]> {
        const documents: WorkspaceDocumentState[] = [];
        for (const host of this.viewerHosts.items) {
            const order = this.getViewablesOrder(host.name);
            let fallbackOrder = 0;
            for (const viewable of host.viewables) {
                if (!(viewable instanceof ViewableScriptDocument) && !(viewable instanceof ViewableFileDocument)) continue;
                const textDocument = viewable.textDocument;
                const history = textDocument.getHistoryState();
                const bookmarkState = getBookmarkManager(viewable.id).getState();
                const view = host.viewers.find(item => item.canOpen(viewable)) as {editor?: ITextEditor} | undefined;
                const isFile = viewable instanceof ViewableFileDocument;
                const state: WorkspaceDocumentState = {
                    id: viewable.id,
                    kind: isFile ? "file" : "script",
                    name: viewable.name,
                    path: viewable.path,
                    source: isFile ? viewable.fileSource : undefined,
                    hostName: host.name,
                    order: Math.max(0, order.indexOf(viewable.id) >= 0 ? order.indexOf(viewable.id) : fallbackOrder),
                    isDirty: viewable.isDirty,
                    buffer: viewable.isDirty ? textDocument.text : undefined,
                    diskFingerprint: isFile ? await fingerprint(viewable.savedTextSnapshot) : undefined,
                    viewState: view?.editor?.getViewState(textDocument.id) ?? textDocument.viewState,
                    bookmarks: bookmarkState.bookmarks,
                    numberedBookmarks: bookmarkState.numberedBookmarks,
                    undoHistory: history.undo,
                    redoHistory: history.redo,
                };
                documents.push(state);
                fallbackOrder++;
            }
        }
        return documents;
    }

    private loadRecentFilePaths(): void {
        this._recentFiles = (this.currentWorkspace.recentFiles ?? [])
            .filter(recent => recent && typeof recent.path === "string" && recent.path.length > 0
                && (recent.source === "library" || recent.source === "native"))
            .slice(0, 15)
            .map(recent => ({
                handle: `workspace-recent:${recent.source}:${recent.path}`,
                path: recent.path,
                source: recent.source === "library" ? "library" : undefined,
            }));
        for (const callback of this.recentFilesChanged) callback();
    }

    private persistRecentFilePaths(): void {
        this.currentWorkspace.recentFiles = this._recentFiles.map(file => ({
            path: file.path,
            source: file.source === "library" ? "library" : "native",
        }));
        this.scheduleWorkspaceSave();
    }

    private updateAutomaticShelf(workspace: WorkspaceState): void {
        const id = `automatic-recovery:${workspace.id}`;
        const documents = workspace.documents.filter(document => document.isDirty);
        const index = this.workspaceManifest.shelves.findIndex(shelf => shelf.id === id);
        if (documents.length === 0) {
            if (index >= 0) this.workspaceManifest.shelves.splice(index, 1);
            return;
        }

        const shelf: WorkspaceShelf = {
            id,
            name: `Automatic recovery: ${workspace.name}`,
            createdUtc: new Date().toISOString(),
            isAutomatic: true,
            state: {...workspace, documents: documents.map(document => ({...document}))},
        };
        if (index >= 0) this.workspaceManifest.shelves[index] = shelf;
        else this.workspaceManifest.shelves.push(shelf);
    }

    private loadLegacyTabOrders(): void {
        if (this.workspaceManifest.isInitialized) return;
        try {
            for (let index = 0; index < localStorage.length; index++) {
                const key = localStorage.key(index);
                if (!key?.startsWith("tab-bar.") || !key.endsWith(".viewables-order")) continue;
                const hostName = key.slice("tab-bar.".length, -".viewables-order".length);
                const value: unknown = JSON.parse(localStorage.getItem(key) ?? "null");
                if (!Array.isArray(value) || value.some(id => typeof id !== "string")) continue;
                this.tabOrders.set(hostName, value as string[]);
                this.pendingLegacyOrderKeys.add(key);
            }
        } catch (error) {
            console.error("Could not migrate tab ordering from local storage", error);
        }
    }
}

async function fingerprint(text: string): Promise<string> {
    const bytes = new TextEncoder().encode(text);
    if (globalThis.crypto?.subtle) {
        const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
        return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("");
    }

    let first = 2166136261;
    let second = 5381;
    for (const byte of bytes) {
        first = Math.imul(first ^ byte, 16777619);
        second = Math.imul(second ^ byte, 33);
    }
    return `${first >>> 0}:${second >>> 0}`;
}
