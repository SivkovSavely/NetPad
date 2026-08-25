import {DisposableCollection} from "@common";
import {ScriptEnvironment, Settings} from "@application";
import {DumpContainer} from "./components/dump-container";
import {SqlViewDumpContainer} from "./components/sql-view/sql-view-dump-container";

export interface IUserInputRequest {
    commandId: string;
    userInput?: string | undefined;
    masked?: boolean;
}

/**
 * Represents the output of a single script.
 */
export class OutputModel {
    public inputRequest?: IUserInputRequest | null;
    private disposables = new DisposableCollection();

    // Named result panels (Util.OpenPanel), in creation order.
    public panels = new Map<string, DumpContainer>();
    public panelNames: string[] = [];
    private readonly settings: Settings;

    public constructor(public environment: ScriptEnvironment, settings: Settings) {
        this.settings = settings;
        this.resultsDumpContainer = new DumpContainer(settings);
        this.sqlDumpContainer = new SqlViewDumpContainer(settings);
    }

    public resultsDumpContainer: DumpContainer;
    public sqlDumpContainer: SqlViewDumpContainer;

    /**
     * Gets or creates the dump container for a named result panel.
     */
    public getOrCreatePanel(name: string): DumpContainer {
        let container = this.panels.get(name);

        if (!container) {
            container = new DumpContainer(this.settings);
            this.panels.set(name, container);
            this.panelNames = [...this.panels.keys()];
        }

        return container;
    }

    public removePanel(name: string) {
        const container = this.panels.get(name);
        if (!container) return;

        container.dispose();
        this.panels.delete(name);
        this.panelNames = [...this.panels.keys()];
    }

    public clearPanels() {
        for (const name of [...this.panels.keys()]) {
            this.removePanel(name);
        }
    }

    public destroy() {
        this.disposables.dispose();
        this.resultsDumpContainer.dispose();
        this.sqlDumpContainer.dispose();
        this.clearPanels();
        this.inputRequest = null;
    }

    public toDto() {
        return {
            scriptId: this.environment.script.id,
            inputRequest: this.inputRequest,
            resultsDumpContainer: {
                html: this.resultsDumpContainer.getHtml(),
                lastOutputOrder: this.resultsDumpContainer.lastOutputOrder,
                scrollOnOutput: this.resultsDumpContainer.scrollOnOutput,
                textWrap: this.resultsDumpContainer.textWrap
            },
            sqlDumpContainer: {
                html: this.sqlDumpContainer.getHtml(),
                lastOutputOrder: this.sqlDumpContainer.lastOutputOrder,
                scrollOnOutput: this.sqlDumpContainer.scrollOnOutput,
                textWrap: this.sqlDumpContainer.textWrap
            },
            panels: this.panelNames.map(name => ({
                name,
                html: this.panels.get(name)!.getHtml()
            }))
        };
    }
}

/**
 * A serializable representation of the IOutputModel.
 */
export interface IOutputModelDto {
    scriptId: string;
    inputRequest?: IUserInputRequest | null;
    resultsDumpContainer: IDumpContainerDto;
    sqlDumpContainer: IDumpContainerDto;
    panels?: IPanelDto[];
}

export interface IPanelDto {
    name: string;
    html: string;
}

/**
 * A serializable representation of the DumpContainer.
 */
export interface IDumpContainerDto {
    html: string;
    lastOutputOrder: number;
    scrollOnOutput: boolean;
    textWrap: boolean;
}
