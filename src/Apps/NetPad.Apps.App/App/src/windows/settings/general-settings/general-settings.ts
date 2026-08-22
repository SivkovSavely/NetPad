import {bindable} from "aurelia";
import {DotNetFrameworkVersion, IAppService, Settings} from "@application";

export class GeneralSettings {
    @bindable public settings: Settings;
    public currentSettings: Readonly<Settings>;
    public availableFrameworkVersions: DotNetFrameworkVersion[] = [];

    constructor(currentSettings: Settings, @IAppService private readonly appService: IAppService) {
        this.currentSettings = currentSettings;
    }

    public attached() {
        this.appService.getAvailableDotNetSdkVersions()
            .then(result => this.availableFrameworkVersions = result);
    }
}
