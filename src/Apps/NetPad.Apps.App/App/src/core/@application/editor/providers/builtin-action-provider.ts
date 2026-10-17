import * as monaco from "monaco-editor";
import {
    ApiException,
    IActionProvider,
    INotificationService,
    ISession,
    MonacoEditorUtil,
    RecentScriptsStore,
    ScriptEnvironment,
    ScriptKind,
    ScriptsStore,
    ScriptSummary
} from "@application";
import {ProblemDetails} from "@application/api-problem-details";
import {IBenchmarkService} from "@application/benchmark/ibenchmark-service";

export class BuiltinActionProvider implements IActionProvider {
    constructor(
        @ISession private readonly session: ISession,
        private readonly scriptsStore: ScriptsStore,
        private readonly recentScriptsStore: RecentScriptsStore,
        @IBenchmarkService private readonly benchmarkService: IBenchmarkService,
        @INotificationService private readonly notificationService: INotificationService) {
    }

    public provideActions(): monaco.editor.IActionDescriptor[] {
        return [
            {
                id: "netpad.action.benchmarkSelection",
                label: "Benchmark Selection",
                run: async editor => {
                    const scriptId = this.getScriptId(editor);
                    if (!scriptId) return;

                    const environment = this.session.environments.find(env => env.script.id === scriptId);
                    if (environment?.script.config.kind === "SQL") {
                        this.notificationService.notify(
                            "Benchmark selection requires a C# script.",
                            "Transient",
                            "Info",
                            scriptId);
                        return;
                    }

                    const code = this.getSelectedCode(editor, scriptId);
                    if (code === undefined) return;

                    try {
                        const operationId = await this.benchmarkService.start(scriptId, code);
                        if (!operationId) throw new Error("The benchmark request did not return an operation id.");
                        this.notificationService.notify(
                            "Benchmark started. Progress and results are available in the Output pane.",
                            "Notice",
                            "Info",
                            scriptId);
                    } catch (error) {
                        this.notificationService.notify(
                            `Could not start benchmark: ${this.getErrorMessage(error)}`,
                            "Alert",
                            "Error",
                            scriptId);
                    }
                }
            },
            {
                id: "netpad.action.cancelBenchmark",
                label: "Cancel Benchmark",
                run: async editor => {
                    const scriptId = this.getScriptId(editor);
                    if (!scriptId) return;

                    try {
                        const cancelled = await this.benchmarkService.cancelLatest(scriptId);
                        this.notificationService.notify(
                            cancelled ? "Benchmark cancelled." : "No active benchmark for this script.",
                            "Transient",
                            "Info",
                            scriptId);
                    } catch (error) {
                        this.notificationService.notify(
                            `Could not cancel benchmark: ${this.getErrorMessage(error)}`,
                            "Alert",
                            "Error",
                            scriptId);
                    }
                }
            },
            {
                id: "netpad.action.transformToUpperOrLowercase",
                label: "Transform to Upper/Lower Case",
                keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyY],
                run: (editor) => {
                    const model = editor?.getModel();
                    const currentSelection = editor?.getSelection();

                    if (!editor || !model || !currentSelection) return;

                    const selectedValue = model.getValueInRange(currentSelection);

                    if (!selectedValue.trim()) return;

                    if (selectedValue === selectedValue.toLowerCase()) {
                        editor.trigger(null, "editor.action.transformToUppercase", null);
                    } else {
                        editor.trigger(null, "editor.action.transformToLowercase", null);
                    }
                }
            },
            {
                id: "netpad.action.goToScript",
                label: "Go to Script",
                keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyT],
                run: () => {
                    const picks = this.buildPicks(
                        [...this.session.environments],
                        this.scriptsStore.scripts,
                        this.recentScriptsStore.recentScripts,
                        this.session.active?.script.id
                    );

                    // matchOnDescription lets typing filter by path/folder, not just the script name.
                    MonacoEditorUtil.getQuickInputService()
                        .pick(picks, {placeholder: "Go to script", matchOnDescription: true})
                        .then((selected: ScriptPick | undefined) => {
                            if (selected) {
                                this.open(selected.script);
                            }
                        });
                }
            }
        ];
    }

    private getScriptId(editor: monaco.editor.ICodeEditor | undefined): string | undefined {
        const model = editor?.getModel();
        if (!model || model.uri.scheme !== "inmemory" || model.uri.authority !== "model") return;

        const scriptId = MonacoEditorUtil.getScriptId(model);
        if (!this.session.environments.some(environment => environment.script.id === scriptId)) return;
        return scriptId;
    }

    private getSelectedCode(editor: monaco.editor.ICodeEditor, scriptId: string): string | undefined {
        const model = editor.getModel();
        const selection = editor.getSelection();
        if (!model || !selection || selection.isEmpty()) {
            this.notificationService.notify("Select code in a script before starting a benchmark.", "Transient", "Info", scriptId);
            return;
        }

        const code = model.getValueInRange(selection);
        if (!code.trim()) {
            this.notificationService.notify("Select code in a script before starting a benchmark.", "Transient", "Info", scriptId);
            return;
        }

        return code;
    }

    private getErrorMessage(error: unknown): string {
        if (error instanceof ApiException) {
            try {
                return ProblemDetails.fromJS(JSON.parse(error.response)).detail || error.message;
            } catch {
                return error.message;
            }
        }
        return error instanceof Error ? error.message : String(error);
    }

    /**
     * Builds the grouped quick-pick list: open scripts (active first), then recents (excluding open
     * ones), then the full library (excluding open ones). A recent that is also a saved script is
     * rendered with that script's name/kind and still appears under "Library"; a recent that is
     * neither open nor in the library is shown by file name and opened by path.
     */
    private buildPicks(
        opened: readonly ScriptEnvironment[],
        libraryScripts: readonly ScriptSummary[],
        recentScripts: readonly string[],
        activeScriptId: string | undefined
    ): GoToScriptPick[] {
        const picks: GoToScriptPick[] = [];

        const openEnvIds = new Set(opened.map(e => e.script.id));
        const openEnvPaths = new Set(
            opened.map(e => e.script.path).filter(p => !!p)
        );

        // Open scripts (active first)
        if (opened.length) {
            picks.push({type: "separator", label: "Open"});
            picks.push(...opened
                .map(env => this.toPick({
                    id: env.script.id,
                    name: env.script.name,
                    kind: env.script.config.kind,
                    path: env.script.path,
                    isDirty: env.script.isDirty
                }))
                .sort((a, b) => Number(b.id === activeScriptId) - Number(a.id === activeScriptId))
            );
        }

        // Index library scripts by path so a recent that is also a saved script renders with its real
        // name/kind (it still appears under "Library" too)
        const libScriptsByPath = new Map<string, ScriptSummary>();
        for (const s of libraryScripts) {
            if (s.path) libScriptsByPath.set(s.path, s);
        }

        // Recents (most-recent-first), excluding any that are already open
        const recents = recentScripts.filter(path => !openEnvPaths.has(path));
        if (recents.length) {
            picks.push({type: "separator", label: "Recent"});
            picks.push(...recents.map(path => {
                const inLib = libScriptsByPath.get(path);
                return inLib ? this.toPick(inLib) : this.toRecentPick(path);
            }));
        }

        // All library scripts, excluding those already open
        const libraryPicks = libraryScripts
            .filter(s => !openEnvIds.has(s.id))
            .map(script => this.toPick(script))
            .sort((a, b) => a.label > b.label ? 1 : -1);
        if (libraryPicks.length) {
            picks.push({type: "separator", label: "Library"});
            picks.push(...libraryPicks);
        }

        return picks;
    }

    private open(script: ScriptRef) {
        if (script.path) {
            const path = script.path;
            this.session.openByPath(path).catch(err => {
                // A recents-only entry (no saved-script id) that no longer exists on disk: prune it so
                // it stops surfacing. Other failures are transient and left in place.
                if (!script.id && err instanceof ApiException && err.status === 404) {
                    this.recentScriptsStore.remove(path).catch(() => undefined);
                }
            });
        } else if (script.id) {
            this.session.activate(script.id);
        }
    }

    private toPick(script: {
        id: string;
        name: string;
        kind: ScriptKind;
        path?: string;
        isDirty?: boolean
    }): ScriptPick {
        const icon = script.kind === "SQL" ? "$(sql)" : "$(csharp)";

        return {
            type: "item",
            id: script.id,
            label: `${icon} ${script.name}`,
            description: !script.path ? "(New)" : ((script.isDirty ? "(Unsaved) " : "") + script.path),
            script
        };
    }

    /**
     * Builds a pick for a recent script known only by its file path.
     * Kind is unknown, so a neutral file icon is used. Selecting it opens by path.
     */
    private toRecentPick(path: string): ScriptPick {
        const fileName = this.fileName(path).replace(/\.netpad$/i, "");
        return {
            type: "item",
            id: `recent:${path}`,
            label: `$(file) ${fileName}`,
            description: path,
            script: {path}
        };
    }

    private fileName(path: string): string {
        const normalized = path.replace(/\\/g, "/");
        return normalized.substring(normalized.lastIndexOf("/") + 1) || path;
    }
}

interface ScriptRef {
    id?: string;
    path?: string;
}

interface ScriptPick {
    type: "item";
    id: string;
    label: string;
    description: string;
    script: ScriptRef;
}

interface ScriptSeparator {
    type: "separator";
    label: string;
}

type GoToScriptPick = ScriptPick | ScriptSeparator;
