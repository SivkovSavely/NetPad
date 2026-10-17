import {IMainMenuService} from "@application/main-menu/imain-menu-service";
import {IMenuItem} from "@application/main-menu/imenu-item";
import {ISettingsService} from "@application/configuration/isettings-service";
import {MonacoEditorUtil} from "@application/editor/monaco/monaco-editor-util";

export interface FeatureSearchSettingsTab {
    route: string;
    text: string;
    description?: string;
    helpText?: string;
    aliases?: string[];
}

export interface FeatureSearchEntry {
    label: string;
    description?: string;
    helpText?: string;
    aliases?: string[];
    action: () => Promise<unknown> | void;
}

export function fuzzyMatchScore(query: string, entry: Pick<FeatureSearchEntry, "label" | "description" | "helpText" | "aliases">): number | undefined {
    const terms = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    if (!terms.length) return 0;

    const fields = [entry.label, entry.description, entry.helpText, ...(entry.aliases ?? [])]
        .filter((value): value is string => !!value)
        .map(value => value.toLocaleLowerCase());
    let total = 0;

    for (const term of terms) {
        let best = Infinity;
        for (const field of fields) {
            let cursor = 0;
            let score = 0;
            for (const character of term) {
                const found = field.indexOf(character, cursor);
                if (found < 0) { score = Infinity; break; }
                score += found === cursor ? 0 : found - cursor;
                cursor = found + 1;
            }
            if (score < best) best = score;
        }
        if (!Number.isFinite(best)) return undefined;
        total += best;
    }
    return total;
}

export function searchFeatureEntries(entries: readonly FeatureSearchEntry[], query: string): FeatureSearchEntry[] {
    return entries
        .map((entry, index) => ({entry, index, score: fuzzyMatchScore(query, entry)}))
        .filter((item): item is typeof item & {score: number} => item.score !== undefined)
        .sort((a, b) => a.score - b.score || a.index - b.index)
        .map(item => item.entry);
}

export class FeatureSearchService {
    constructor(
        @IMainMenuService private readonly mainMenu: IMainMenuService,
        @ISettingsService private readonly settingsService: ISettingsService) {
    }

    public async open(settingsTabs: readonly FeatureSearchSettingsTab[]): Promise<void> {
        await this.mainMenu.initialized;
        const entries = this.getEntries(settingsTabs);
        const quickInput = MonacoEditorUtil.getQuickInputService();
        const selected = await quickInput.pick(entries.map((entry, index) => ({
            type: "item" as const,
            id: String(index),
            label: entry.label,
            description: entry.description,
            detail: entry.helpText,
            entry
        })), {placeholder: "Search features and settings", matchOnDescription: true, matchOnDetail: true});
        await selected?.entry.action();
    }

    public getEntries(settingsTabs: readonly FeatureSearchSettingsTab[]): FeatureSearchEntry[] {
        const entries: FeatureSearchEntry[] = [];
        const addMenu = (items: readonly IMenuItem[], parents: string[] = []) => {
            for (const item of items) {
                if (item.menuItems) addMenu(item.menuItems, item.text ? [...parents, item.text] : parents);
                if (!item.text || item.isDivider || item.disabled || (!item.click && !item.id)) continue;
                entries.push({
                    label: [...parents, item.text].join(" / "),
                    description: item.hoverText,
                    helpText: item.helpText,
                    action: () => item.click ? item.click() : this.mainMenu.clickMenuItem(item)
                });
            }
        };
        addMenu(this.mainMenu.items);
        for (const tab of settingsTabs) {
            entries.push({
                label: `Settings / ${tab.text}`,
                description: tab.description,
                helpText: tab.helpText,
                aliases: tab.aliases,
                action: () => this.settingsService.openSettingsWindow(tab.route)
            });
        }
        return entries;
    }
}
