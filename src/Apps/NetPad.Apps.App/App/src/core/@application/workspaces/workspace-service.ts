import {DI} from "aurelia";
import {IHttpClient} from "@aurelia/fetch-client";

export interface WorkspaceDocumentState {
    id: string;
    kind: "script" | "file";
    name?: string;
    path?: string;
    source?: "library" | "native";
    hostName: string;
    order: number;
    isDirty: boolean;
    buffer?: string;
    diskFingerprint?: string;
    diskModifiedUtc?: string;
    viewState?: unknown;
    bookmarks: number[];
    numberedBookmarks: Record<string, number>;
    undoHistory: string[];
    redoHistory: string[];
}

export interface WorkspaceState {
    id: string;
    name: string;
    documents: WorkspaceDocumentState[];
    activeDocumentId?: string;
    activeHostName: string;
}

export interface WorkspaceShelf {
    id: string;
    name: string;
    createdUtc: string;
    isAutomatic?: boolean;
    state: WorkspaceState;
}

export interface WorkspaceManifest {
    version: number;
    isInitialized: boolean;
    activeWorkspaceId: string;
    workspaces: WorkspaceState[];
    shelves: WorkspaceShelf[];
}

export interface IWorkspaceService {
    readonly manifest: WorkspaceManifest;
    initialize(): Promise<void>;
    save(keepalive?: boolean): Promise<void>;
}

export const IWorkspaceService = DI.createInterface<IWorkspaceService>();

export class WorkspaceService implements IWorkspaceService {
    private _manifest: WorkspaceManifest = {
        version: 1,
        isInitialized: false,
        activeWorkspaceId: "default",
        workspaces: [{id: "default", name: "Default", documents: [], activeHostName: "main"}],
        shelves: [],
    };
    private initialization?: Promise<void>;
    private saveQueue: Promise<void> = Promise.resolve();

    constructor(@IHttpClient private readonly http: IHttpClient) {
    }

    public get manifest(): WorkspaceManifest {
        return this._manifest;
    }

    public initialize(): Promise<void> {
        if (!this.initialization) this.initialization = this.load();
        return this.initialization;
    }

    public async save(keepalive = false): Promise<void> {
        this._manifest.version = 1;
        this._manifest.isInitialized = true;
        const body = JSON.stringify(this._manifest);
        const request = this.saveQueue.catch(() => undefined).then(async () => {
            const response = await this.http.fetch("/workspaces", {
                method: "PUT",
                headers: {"Content-Type": "application/json"},
                body,
                keepalive,
            });
            if (!response.ok) throw new Error(await response.text() || `Could not save workspace state (${response.status})`);
        });
        this.saveQueue = request;
        await request;
    }

    private async load(): Promise<void> {
        try {
            const response = await this.http.fetch("/workspaces");
            if (!response.ok) throw new Error(await response.text() || `Could not load workspace state (${response.status})`);

            const loaded = await response.json() as Partial<WorkspaceManifest>;
            this._manifest = {
                version: typeof loaded.version === "number" ? loaded.version : 1,
                isInitialized: loaded.isInitialized === true,
                activeWorkspaceId: loaded.activeWorkspaceId || "default",
                workspaces: loaded.workspaces?.filter(workspace => workspace && workspace.id)
                    .map(workspace => ({
                        ...workspace,
                        documents: workspace.documents ?? [],
                        activeHostName: workspace.activeHostName || "main",
                    })) ?? [],
                shelves: loaded.shelves ?? [],
            };

            if (this._manifest.workspaces.length === 0) {
                this._manifest.workspaces.push({id: "default", name: "Default", documents: [], activeHostName: "main"});
                this._manifest.activeWorkspaceId = "default";
            }
            if (!this._manifest.workspaces.some(workspace => workspace.id === this._manifest.activeWorkspaceId)) {
                this._manifest.activeWorkspaceId = this._manifest.workspaces[0].id;
            }
        } catch (error) {
            console.error("Could not load workspace state; using the default workspace.", error);
        }
    }
}
