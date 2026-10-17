import {IHttpClient} from "@aurelia/fetch-client";
import {LibraryTextFileService} from "@application/file-search/library-text-file-service";
import {MonacoEditorUtil} from "@application/editor/monaco/monaco-editor-util";
import {ISession} from "@application/sessions/isession";
import {IWorkAreaService} from "../../../windows/main/work-area/work-area-service";
import {ViewableTextDocument} from "../../../windows/main/work-area/viewers/viewable-text-document";
import {IFileDocumentDialogService} from "../../../windows/main/work-area/viewers/file-viewer/file-document-dialog-service";

export interface FileSearchMatch {
    path: string;
    lineNumber: number;
    line: string;
}

export type FileSearchQuickPick =
    | {type: "separator", label: string}
    | {type: "item", label: string, description: string, match: FileSearchMatch};

export function buildFileSearchPicks(matches: readonly FileSearchMatch[]): FileSearchQuickPick[] {
    const picks: FileSearchQuickPick[] = [];
    let currentPath: string | undefined;
    for (const match of matches) {
        if (match.path !== currentPath) {
            currentPath = match.path;
            picks.push({type: "separator", label: currentPath});
        }
        picks.push({
            type: "item",
            label: `Line ${match.lineNumber}`,
            description: match.line,
            match,
        });
    }
    return picks;
}

export class FileSearchQuickInputService {
    private activeRequest?: AbortController;

    constructor(
        @IHttpClient private readonly http: IHttpClient,
        @ISession private readonly session: ISession,
        @IWorkAreaService private readonly workArea: IWorkAreaService,
        private readonly libraryFiles: LibraryTextFileService,
        @IFileDocumentDialogService private readonly dialogUtil: IFileDocumentDialogService,
    ) {
    }

    public async open(): Promise<void> {
        this.activeRequest?.abort();
        const query = await MonacoEditorUtil.getQuickInputService().input({
            prompt: "Search files in script-library roots",
            placeHolder: "Search file contents",
        });
        if (!query?.trim()) return;

        const request = this.activeRequest = new AbortController();
        try {
            const response = await this.http.fetch(`/search?query=${encodeURIComponent(query)}`, {signal: request.signal});
            if (!response.ok) throw new Error(await response.text() || `Search failed (${response.status})`);
            const matches = await response.json() as FileSearchMatch[];
            if (request.signal.aborted || !matches.length) return;

            const selected = await MonacoEditorUtil.getQuickInputService().pick(buildFileSearchPicks(matches), {
                placeholder: `${matches.length} file matches`,
                matchOnDescription: true,
            });
            if (!selected || selected.type !== "item") return;
            await this.openMatch(selected.match);
        } catch (error) {
            if (!request.signal.aborted) {
                await this.dialogUtil.alert({title: "File Search Failed", message: String(error)});
            }
        } finally {
            if (this.activeRequest === request) this.activeRequest = undefined;
        }
    }

    private async openMatch(match: FileSearchMatch): Promise<void> {
        try {
            if (match.path.toLowerCase().endsWith(".netpad")) {
                const environment = await this.session.openByPath(match.path);
                let result = this.workArea.findViewable(environment.script.id);
                if (!result) {
                    const viewable = this.workArea.createScriptViewable(environment);
                    await this.workArea.open(viewable);
                    result = this.workArea.findViewable(viewable.id);
                }
                if (result?.viewable instanceof ViewableTextDocument) {
                    result.viewable.textDocument.pendingLine = match.lineNumber;
                    result.host.activate(result.viewable);
                    await this.workArea.viewerHosts.activate(result.host);
                }
            } else {
                const file = await this.libraryFiles.openTextFile(match.path);
                await this.workArea.openFile(file, match.lineNumber);
            }
        } catch (error) {
            await this.dialogUtil.alert({title: "Could Not Open Search Result", message: String(error)});
        }
    }
}
