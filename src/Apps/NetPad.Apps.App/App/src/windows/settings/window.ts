import {IContainer} from "aurelia";
import {ISettingsService, IWindowService, MonacoEnvironmentManager, Settings} from "@application";
import {WindowBase} from "@application/windows/window-base";
import {WindowParams} from "@application/windows/window-params";
import {findSettingsMatches, indexSettingsControls, SettingsSearchEntry} from "./settings-search";

export const SettingsTabs = [
    {route: "general", text: "General"},
    {route: "editor", text: "Editor"},
    {route: "results", text: "Results"},
    {route: "style", text: "Styles"},
    {route: "keyboard-shortcuts", text: "Keyboard Shortcuts"},
    {route: "omnisharp", text: "OmniSharp"},
    {route: "about", text: "About"},
];

export class Window extends WindowBase {
    public editableSettings: Settings;
    public selectedTab;
    public searchQuery = "";
    public searchResults: SettingsSearchEntry[] = [];
    private settingsRoot: HTMLElement;
    private searchableSettingsRoot: HTMLElement;
    private searchTimer?: ReturnType<typeof setTimeout>;
    public tabs = SettingsTabs;

    constructor(
        @ISettingsService private readonly settingsService: ISettingsService,
        @IWindowService private readonly windowService: IWindowService,
        @IContainer private readonly container: IContainer) {
        super();

        document.title = "Settings";

        let tabIndex = this.tabs.findIndex(t => t.route === WindowParams.get("tab"));
        if (tabIndex < 0)
            tabIndex = 0;

        this.selectedTab = this.tabs[tabIndex];
        this.editableSettings = this.settings.clone();
    }

    public async binding() {
        await MonacoEnvironmentManager.setupMonacoEnvironment(this.container);
    }

    public attached() {
        this.indexAllSettings();
    }

    public selectedTabChanged() {
        if (this.searchTimer) clearTimeout(this.searchTimer);
        this.searchTimer = setTimeout(() => this.indexAllSettings(), 0);
    }

    public search(query: string) {
        this.searchQuery = query;
        if (this.searchTimer) clearTimeout(this.searchTimer);
        this.searchTimer = setTimeout(() => this.indexAllSettings(), 0);
    }

    public selectSearchResult(entry: SettingsSearchEntry) {
        const tab = this.tabs.find(tab => tab.route === entry.tabRoute);
        if (!tab) return;
        this.selectedTab = tab;
        setTimeout(() => {
            const root = this.settingsRoot && [...this.settingsRoot.children]
                .find(child => child.getAttribute("case") === entry.tabRoute) as HTMLElement | undefined;
            if (!root) return;
            const current = [...root.querySelectorAll<HTMLElement>("[id]")]
                .find(control => control.id === entry.controlId);
            current?.scrollIntoView({block: "center", behavior: "smooth"});
            current?.focus();
        }, 0);
    }

    private indexAllSettings() {
        if (!this.searchQuery.trim() || !this.searchableSettingsRoot) {
            this.searchResults = [];
            return;
        }
        const entries = SettingsTabs.flatMap(tab => {
            const root = [...this.searchableSettingsRoot.children]
                .find(child => child.getAttribute("data-settings-route") === tab.route) as HTMLElement | undefined;
            return root ? indexSettingsControls(root, tab.route, tab.text) : [];
        });
        this.searchResults = findSettingsMatches(entries, this.searchQuery);
    }

    public get canApply() {
        return JSON.stringify(this.settings) !== JSON.stringify(this.editableSettings);
    }

    public async apply(): Promise<boolean> {
        if (!this.validate()) {
            return false;
        }

        try {
            await this.settingsService.update(this.editableSettings);
            return true;
        } catch (e) {
            this.logger.error("Error while saving settings", e);
            alert("A problem occurred. Could not save settings");
            return false;
        }
    }

    public async save() {
        if (!await this.apply()) {
            return;
        }

        await this.windowService.close();
    }

    public async close() {
        await this.windowService.close();
    }

    public async showAppDataFolder() {
        await this.settingsService.showSettingsFile();
    }

    private validate(): boolean {
        let userValue: unknown = this.editableSettings.results.maxSerializationDepth;
        if ((userValue !== 0 && !userValue) || isNaN(Number(userValue))) {
            alert("Results > Serialization > Max Depth is required.");
            return false;
        }

        userValue = this.editableSettings.results.maxCollectionSerializeLength;
        if ((userValue !== 0 && !userValue) || isNaN(Number(userValue))) {
            alert("Results > Serialization > Max Collection Length is required.");
            return false;
        }

        return true;
    }
}
