import {MonacoEditorUtil} from "./monaco/monaco-editor-util";
import {IWorkAreaService} from "../../../windows/main/work-area/work-area-service";
import {ViewableObject} from "../../../windows/main/work-area/viewers/viewable-object";
import {ViewerHost} from "../../../windows/main/work-area/viewers/viewer-host";

export interface OpenViewablePick {
    readonly viewable: ViewableObject;
    readonly host: ViewerHost;
}

export function rankOpenViewables(picks: readonly OpenViewablePick[], query: string, mru: readonly string[]): OpenViewablePick[] {
    const needle = query.trim().toLocaleLowerCase();
    const recency = new Map(mru.map((id, index) => [id, index]));

    return picks
        .map((pick, order) => {
            const haystack = `${pick.viewable.name} ${pick.viewable.path ?? ""}`.toLocaleLowerCase();
            let cursor = 0;
            let score = 0;
            for (const char of needle) {
                const found = haystack.indexOf(char, cursor);
                if (found < 0) return undefined;
                score += found === cursor ? 3 : 1;
                cursor = found + 1;
            }
            return {pick, order, score, recent: recency.get(pick.viewable.id) ?? Number.MAX_SAFE_INTEGER};
        })
        .filter((item): item is NonNullable<typeof item> => !!item)
        .sort((a, b) => b.score - a.score || a.recent - b.recent || a.order - b.order)
        .map(item => item.pick);
}

export class OpenViewableQuickInput {
    private readonly mru: string[] = [];

    constructor(@IWorkAreaService private readonly workAreaService: IWorkAreaService) {}

    public async show(): Promise<void> {
        const picks = this.workAreaService.viewerHosts.items.flatMap(host =>
            [...host.viewables].map(viewable => ({viewable, host})));
        if (!picks.length) return;

        const quickInput = MonacoEditorUtil.getQuickInputService().createQuickPick<OpenViewablePick>();
        quickInput.placeholder = "Go to open viewable";
        quickInput.matchOnDescription = true;
        quickInput.items = this.toItems(rankOpenViewables(picks, "", this.mru));
        quickInput.onDidChangeValue((value: string) => {
            quickInput.items = this.toItems(rankOpenViewables(picks, value, this.mru));
        });
        quickInput.onDidAccept(async () => {
            const selected = quickInput.selectedItems[0];
            if (!selected) return;
            const previous = this.mru.indexOf(selected.viewable.id);
            if (previous >= 0) this.mru.splice(previous, 1);
            this.mru.unshift(selected.viewable.id);
            await this.workAreaService.viewerHosts.activate(selected.host);
            selected.host.activate(selected.viewable);
            quickInput.hide();
        });
        let disposed = false;
        quickInput.onDidHide(() => {
            if (disposed) return;
            disposed = true;
            quickInput.dispose();
        });
        quickInput.show();
    }

    private toItems(picks: OpenViewablePick[]) {
        return picks.map(pick => ({
            label: `${pick.viewable.isDirty ? "$(circle-filled) " : ""}${pick.viewable.name}`,
            description: pick.viewable.path ?? "",
            detail: pick.host.name,
            buttons: [],
            ...pick
        }));
    }
}
