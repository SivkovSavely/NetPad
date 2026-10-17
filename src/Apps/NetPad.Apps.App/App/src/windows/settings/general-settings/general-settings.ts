import {bindable} from "aurelia";
import {DotNetFrameworkVersion, IAppService, Settings} from "@application";

export class GeneralSettings {
    @bindable public settings: Settings;
    @bindable public searchOnly = false;
    public currentSettings: Readonly<Settings>;
    public availableFrameworkVersions: DotNetFrameworkVersion[] = [];

    public get scriptLibraryRootsText(): string {
        return this.settings.scriptLibraryRoots?.join("\n") ?? "";
    }

    public set scriptLibraryRootsText(value: string) {
        this.settings.scriptLibraryRoots = value.split(/\r?\n/).map(root => root.trim()).filter(Boolean);
    }

    constructor(currentSettings: Settings, @IAppService private readonly appService: IAppService) {
        this.currentSettings = currentSettings;
    }

    public attached() {
        if (this.searchOnly) return;
        this.appService.getAvailableDotNetSdkVersions()
            .then(result => this.availableFrameworkVersions = result);
    }
}
