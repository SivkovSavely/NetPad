import {watch} from "@aurelia/runtime-html";
import {
    ContextMenuOptions,
    IAppService,
    IDataConnectionService,
    IScriptService,
    ISession,
    RunOptions,
    ScriptEnvironment,
    ScriptsStore,
    ScriptSummary,
    Settings,
    ViewModelBase
} from "@application";
import {DialogUtil} from "@application/dialogs/dialog-util";
import {Util} from "@common";
import {ILogger} from "aurelia";
import {ScriptFolderViewModel} from "./script-folder-view-model";
import {findMostSpecificScriptRoot, isScriptPathWithin, normalizeScriptRoot} from "./script-path-utils";
import {ScriptViewModel} from "./script-view-model";

export class ScriptsList extends ViewModelBase {
    private readonly rootScriptFolder: ScriptFolderViewModel;
    private scriptsMap: Map<string, ScriptViewModel>;
    public scriptContextMenuOptions: ContextMenuOptions;
    public folderContextMenuOptions: ContextMenuOptions;

    constructor(private readonly element: HTMLElement,
                @ISession private readonly session: ISession,
                @IScriptService private readonly scriptService: IScriptService,
                @IAppService private readonly appService: IAppService,
                @IDataConnectionService private readonly dataConnectionService: IDataConnectionService,
                private readonly dialogUtil: DialogUtil,
                private readonly scriptsStore: ScriptsStore,
                private readonly settings: Settings,
                @ILogger logger: ILogger) {

        super(logger);
        this.scriptsMap = new Map<string, ScriptViewModel>();
        this.rootScriptFolder = new ScriptFolderViewModel("/", "/", null);
        this.rootScriptFolder.expanded = true;
    }

    public binding() {
        this.scriptContextMenuOptions = new ContextMenuOptions(".list-group-item.script", [
            {
                text: "Open",
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script?.path) {
                        await this.session.openByPath(script.path).catch(() => undefined);
                    }
                }
            },
            {
                icon: "run-icon",
                text: "Run",
                show: (target) => {
                    const script = this.getScriptFromElement(target);
                    if (!script?.environment) return false;
                    const status = script.environment.status;
                    return status !== "Running" && status !== "Stopping";
                },
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script) await this.scriptService.run(script.id, new RunOptions());
                }
            },
            {
                icon: "stop-icon text-red",
                text: "Stop",
                show: (target) => {
                    const script = this.getScriptFromElement(target);
                    return script?.environment?.status === "Running";
                },
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script) await this.scriptService.stop(script.id, undefined);
                }
            },
            {
                icon: "rename-icon",
                text: "Rename",
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script) await this.scriptService.openRenamePrompt(script);
                }
            },
            {
                icon: "duplicate-icon",
                text: "Duplicate",
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script) await this.scriptService.duplicate(script.id);
                }
            },
            {
                isDivider: true
            },
            {
                icon: "open-folder-icon",
                text: "Open Containing Folder",
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (script) await this.appService.openFolderContainingScript(script.path);
                }
            },
            {
                isDivider: true
            },
            {
                icon: "delete-icon text-red",
                text: "Delete",
                onSelected: async (target) => {
                    const script = this.getScriptFromElement(target);
                    if (!script) return;

                    const confirmation = await this.dialogUtil.ask({
                        message: `Delete '${script.name}'? This cannot be undone.`
                    });

                    if (confirmation.value === "OK") {
                        await this.scriptService.delete(script.id);
                    }
                }
            }
        ]);

        this.folderContextMenuOptions = new ContextMenuOptions(".list-group-item.script-folder", [
            {
                icon: "script-folder-open-icon",
                text: "Open in File Manager",
                onSelected: async (target) => {
                    const folder = this.getFolderFromElement(target);
                    if (folder) await this.appService.openScriptsFolder(folder.path);
                }
            },
            {
                isDivider: true
            },
            {
                icon: "delete-icon text-red",
                text: "Delete",
                show: (target) => {
                    const folder = this.getFolderFromElement(target);
                    return folder != null && folder.path !== "/" && !folder.isLibraryRoot &&
                        (!this.hasAdditionalRoots() || !this.isInAdditionalRoot(folder.path));
                },
                onSelected: async (target) => {
                    const folder = this.getFolderFromElement(target);
                    if (!folder) {
                        return;
                    }

                    const confirmation = await this.dialogUtil.ask({
                        message: `Delete folder '${folder.name}' and the ${folder.containingScriptCount} scripts it contains? This cannot be undone.`
                    });

                    if (confirmation.value === "OK") {
                        await this.scriptService.deleteFolder(folder.path);
                    }
                }
            }
        ]);
    }

    public attached() {
        this.loadScripts(this.scriptsStore.scripts);
        this.addDisposable(this.scriptsStore.onChanged(() => this.loadScripts(this.scriptsStore.scripts)));
    }

    public async openScriptsFolder(folder: ScriptFolderViewModel) {
        await this.appService.openScriptsFolder(folder.path);
    }

    public expandAllFolders(folder: ScriptFolderViewModel) {
        folder.expanded = true;
        folder.folders.forEach(f => this.expandAllFolders(f));
    }

    public collapseAllFolders(folder: ScriptFolderViewModel) {
        folder.expanded = false;
        folder.folders.forEach(f => this.collapseAllFolders(f));
    }

    private loadScripts(summaries: ScriptSummary[]) {
        const scripts = summaries.map(s => new ScriptViewModel(s));

        const expandedFolders = this.rootScriptFolder.findFolders(f => f.expanded);

        const root = this.rootScriptFolder.clone();

        const scriptsDirPath = normalizeScriptRoot(this.settings.scriptsDirectoryPath);
        const additionalRoots = [...new Set((this.settings.scriptLibraryRoots ?? [])
            .map(root => normalizeScriptRoot(root)))]
            .filter(root => root && root !== scriptsDirPath);
        const roots = [scriptsDirPath, ...additionalRoots];
        const multiRoot = additionalRoots.length > 0;

        if (multiRoot) {
            for (const rootPath of roots) {
                const rootFolder = new ScriptFolderViewModel(rootPath, rootPath, this.rootScriptFolder);
                rootFolder.isLibraryRoot = true;
                root.folders.push(rootFolder);
            }
        }

        for (const script of scripts) {
            if (!script.path) {
                continue;
            }

            const scriptPath = script.path.replaceAll("\\", "/");
            const matchingRoot = findMostSpecificScriptRoot(scriptPath, roots);
            if (multiRoot && !matchingRoot) continue;

            const basePath = multiRoot ? matchingRoot! : scriptsDirPath;
            const path = multiRoot
                ? "/" + Util.trim(scriptPath.substring(basePath.length), "/")
                : isScriptPathWithin(scriptPath, scriptsDirPath)
                    ? "/" + Util.trim(scriptPath.substring(scriptsDirPath.length), "/")
                    : scriptPath;

            const folderParts = path
                .split("/")
                .filter(x => !!x)
                .slice(0, -1);

            const rootFolder = multiRoot
                ? root.folders.find(folder => folder.path === matchingRoot)!
                : root;
            const folder = this.getFolder(rootFolder, folderParts, multiRoot && matchingRoot !== scriptsDirPath);
            folder.scripts.push(script);
        }

        this.rootScriptFolder.scripts = root.scripts;
        this.rootScriptFolder.folders = root.folders;
        this.rootScriptFolder.updateStats(new Set(expandedFolders.map(f => f.path)));

        this.scriptsMap = new Map<string, ScriptViewModel>(scripts.map(s => [s.id, s]));
        this.hydrateScriptMarkers();
    }

    private getFolder(parent: ScriptFolderViewModel, folderPathParts: string[], absolute = false): ScriptFolderViewModel {
        let result = parent;

        for (let i = 0; i < folderPathParts.length; i++) {
            const folderName = folderPathParts[i];
            let folder = result.folders.find(f => f.name === folderName);
            if (!folder) {
                folder = new ScriptFolderViewModel(folderName,
                    absolute ? `${result.path}/${folderName}` : folderPathParts.slice(0, i + 1).join("/"), result);
                result.folders.push(folder);
            }
            result = folder;
        }

        return result;
    }

    private hasAdditionalRoots(): boolean {
        const primaryRoot = normalizeScriptRoot(this.settings.scriptsDirectoryPath);
        return (this.settings.scriptLibraryRoots ?? [])
            .some(root => normalizeScriptRoot(root) !== primaryRoot);
    }

    private isInAdditionalRoot(path: string): boolean {
        return (this.settings.scriptLibraryRoots ?? []).some(root => {
            const normalized = normalizeScriptRoot(root);
            const primaryRoot = normalizeScriptRoot(this.settings.scriptsDirectoryPath);
            return normalized !== primaryRoot &&
                (path === normalized || isScriptPathWithin(path, normalized));
        });
    }

    @watch<ScriptsList>(vm => vm.session.environments.length)
    private hydrateScriptMarkers() {
        const openEnvs = new Map<string, ScriptEnvironment>(this.session.environments.map(e => [e.script.id, e]));
        for (const script of this.scriptsMap.values()) {
            script.environment = openEnvs.get(script.id);
        }
    }

    @watch<ScriptsList>(vm => vm.session.active)
    private hydrateActiveScript() {
        const activeScriptId = this.session.active?.script.id;

        for (const script of this.scriptsMap.values()) {
            script.isActive = false;
        }

        if (activeScriptId) {
            const script = this.scriptsMap.get(activeScriptId);
            if (script) script.isActive = true;
        }
    }

    private getScriptFromElement(element: Element): ScriptViewModel | undefined {
        const id = this.getElementAttribute(element, "data-script-id");
        return id ? this.scriptsMap.get(id) : undefined;
    }

    private getFolderFromElement(element: Element): ScriptFolderViewModel | undefined {
        const path = this.getElementAttribute(element, "data-folder-path");
        if (!path) {
            return undefined;
        }

        if (this.rootScriptFolder.path === path) {
            return this.rootScriptFolder;
        }

        return this.rootScriptFolder.findFolder(folder => folder.path === path);
    }

    private getElementAttribute(element: Element, attributeName: string): string | null {
        let el: Element | null = element;

        do {
            const value = el.getAttribute(attributeName);
            if (value) {
                return value;
            }
            el = el.parentElement === this.element.parentElement ? null : el.parentElement;
        } while (el);

        return null;
    }
}
