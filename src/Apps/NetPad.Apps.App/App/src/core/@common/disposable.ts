export interface IDisposable {
    dispose(): void;
}

interface DisposableEntry {
    source: IDisposable | (() => void);
    dispose: () => void;
}

export class DisposableCollection implements IDisposable {
    private readonly entries: DisposableEntry[] = [];

    public add(disposable: IDisposable | (() => void)) {
        this.entries.push(disposable instanceof Function
            ? { source: disposable, dispose: disposable }
            : { source: disposable, dispose: () => disposable.dispose() });
    }

    public remove(disposable: IDisposable | (() => void)) {
        const ix = this.entries.findIndex(entry => entry.source === disposable);
        if (ix >= 0) this.entries.splice(ix, 1);
    }

    public get isEmpty() {
        return this.entries.length === 0;
    }

    public dispose() {
        const entries = this.entries.splice(0);

        for (const entry of entries) {
            try {
                entry.dispose();
            } catch (ex) {
                console.error("Error while disposing", entry.source, ex);
            }
        }
    }
}

export abstract class WithDisposables implements IDisposable {
    private readonly disposables = new DisposableCollection();

    public addDisposable(disposable: IDisposable | (() => void)) {
        this.disposables.add(disposable);
    }

    public removeDisposable(disposable: IDisposable | (() => void)) {
        this.disposables.remove(disposable);
    }

    public dispose() {
        this.disposables.dispose();
    }
}
