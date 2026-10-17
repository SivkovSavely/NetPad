import {ViewerHost} from "./viewer-host";
import {ViewableObject} from "./viewable-object";

export class ViewerHostCollection {
    private _items: ViewerHost[] = [];
    private _active?: ViewerHost | null;
    private readonly changeHandlers = new Set<() => void>();

    public get items(): ReadonlyArray<ViewerHost> {
        return this._items;
    }

    public get active(): ViewerHost | undefined | null {
        return this._active;
    }

    public onChanged(callback: () => void): {dispose(): void} {
        this.changeHandlers.add(callback);
        return {dispose: () => this.changeHandlers.delete(callback)};
    }

    public add(viewerHost: ViewerHost) {
        if (this._items.some(x => x.id == viewerHost.id)) {
            throw new Error(`A ${nameof(ViewerHost)} with ID '${viewerHost.id}' already exists`);
        }

        const position = this._items.length;
        viewerHost.order = position;
        viewerHost.name = position === 0 ? "main" : `split-${position}`;
        this._items.push(viewerHost);
        viewerHost.onChanged(() => this.fireChanged());
        this.fireChanged();
    }

    public async activate(viewerHost: ViewerHost | null) {
        this._active = viewerHost;
        await viewerHost?.activeViewable?.activate(viewerHost);
        this.fireChanged();
    }

    public findViewable(viewableId: string): { viewable: ViewableObject, host: ViewerHost } | undefined {
        for (const host of this._items) {
            const viewable = host.find(viewableId);
            if (viewable)
                return {viewable, host};
        }

        return undefined;
    }

    private fireChanged(): void {
        for (const callback of this.changeHandlers) callback();
    }
}
