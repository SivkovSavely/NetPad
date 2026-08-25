import {bindable, PLATFORM, resolve} from "aurelia";
import {ILogger} from "aurelia";
import {OutputModel} from "../output-model";
import {DumpContainer} from "./dump-container";

/**
 * Renders the dump container of a single named result panel for the current output model.
 */
export class PanelView {
    @bindable public model: OutputModel;
    @bindable public name: string;
    public container?: DumpContainer;
    private wrapper?: Element;
    private readonly logger: ILogger;

    constructor() {
        this.logger = resolve(ILogger);
    }

    public attached() {
        this.attach();
    }

    public unbinding() {
        this.container?.detachingFromDom();
    }

    private modelChanged() {
        if (this.wrapper) {
            this.attach();
        }
    }

    private nameChanged() {
        if (this.wrapper) {
            this.attach();
        }
    }

    private attach() {
        const container = this.model?.getOrCreatePanel(this.name);
        if (!container || !this.wrapper) return;

        if (this.container === container) return;

        PLATFORM.queueMicrotask(() => {
            try {
                this.container?.detachingFromDom();
                this.container = container;
                this.wrapper!.replaceChildren(container.element);
                container.attachedToDom();
            } catch (err) {
                this.logger.error(`Failed to attach panel '${this.name}'`, err);
            }
        });
    }
}
