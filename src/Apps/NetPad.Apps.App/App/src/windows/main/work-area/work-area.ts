import {ILogger} from "aurelia";
import {watch} from "@aurelia/runtime-html";
import {ISession, ScriptEnvironment} from "@application";
import {ViewModelBase} from "@application/view-model-base";
import {ViewableObject} from "./viewers/viewable-object";
import {ViewableScriptDocument} from "./viewers/script-viewer/viewable-script-document";
import {Workbench} from "../workbench";

export class WorkArea extends ViewModelBase {
    constructor(
        private readonly workbench: Workbench,
        @ISession private readonly session: ISession,
        @ILogger logger: ILogger,
    ) {
        super(logger);
    }

    protected override async attaching() {
        super.attaching();

        const service = this.workbench.workAreaService;
        await service.restoreWorkspace();

        // Fallback: activate the first viewable in each host that has no active viewable yet.
        for (const viewerHost of service.viewerHosts.items.filter(x => !x.activeViewable && x.viewables.size > 0)) {
            const [viewable] = viewerHost.viewables;
            viewerHost.activate(viewable);
        }

        const active = service.activeViewable;
        document.title = active ? `${active.name} - NetPad` : "NetPad";
    }

    @watch<WorkArea>(vm => vm.session.environments.length)
    private async environmentsChanged() {
        const environments = this.session.environments;
        const service = this.workbench.workAreaService;
        if (service.isRestoringWorkspace) return;

        // Additions: create a viewable for each new environment and open it.
        for (const environment of environments) {
            if (service.viewerHosts.items.some(vh => vh.find(environment.script.id)))
                continue;

            const viewable = service.createScriptViewable(environment);
            await service.open(viewable);
        }

        // Removals: remove viewables whose environment is gone.
        for (const viewerHost of service.viewerHosts.items) {
            const removed: ViewableObject[] = [];

            for (const viewable of viewerHost.viewables) {
                if (!(viewable instanceof ViewableScriptDocument))
                    continue;

                if (!environments.some(e => e.script.id === viewable.id))
                    removed.push(viewable);
            }

            viewerHost.removeViewables(...removed);

            for (const viewable of removed) {
                viewable.dispose();
            }
        }
    }

    @watch<WorkArea>(vm => vm.session.active)
    private async activeEnvironmentChanged(newActive: ScriptEnvironment | null | undefined) {
        let documentTitle: string | undefined;

        try {
            if (!newActive) {
                return;
            }

            const result = this.workbench.workAreaService.findViewable(newActive.script.id);
            if (result) {
                result.host.activate(result.viewable);
                await this.workbench.workAreaService.viewerHosts.activate(result.host);
                documentTitle = result.viewable.name;
            }
        } finally {
            document.title = documentTitle ? `${documentTitle} - NetPad` : "NetPad";
        }
    }
}
