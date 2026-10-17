import {ApiException, BenchmarkApiClient} from "@application/api";
import {IBenchmarkService} from "./ibenchmark-service";

export class BenchmarkService extends BenchmarkApiClient implements IBenchmarkService {
    private readonly operationIds = new Map<string, string>();
    private readonly scriptQueues = new Map<string, Promise<void>>();

    public override async start(scriptId: string, selectedCode: string, signal?: AbortSignal): Promise<string> {
        return this.runExclusive(scriptId, async () => {
            await this.cancelTracked(scriptId);
            const operationId = await super.start(scriptId, selectedCode, signal);
            if (operationId) this.operationIds.set(scriptId, operationId);
            return operationId;
        });
    }

    public async cancelLatest(scriptId: string): Promise<boolean> {
        return this.runExclusive(scriptId, () => this.cancelTracked(scriptId));
    }

    private async cancelTracked(scriptId: string): Promise<boolean> {
        const operationId = this.operationIds.get(scriptId);
        if (!operationId) return false;

        try {
            await super.cancel(scriptId, operationId);
        } catch (error) {
            if (!(error instanceof ApiException) || error.status !== 404) throw error;
            this.operationIds.delete(scriptId);
            return false;
        }

        this.operationIds.delete(scriptId);
        return true;
    }

    private runExclusive<T>(scriptId: string, operation: () => Promise<T>): Promise<T> {
        const previous = this.scriptQueues.get(scriptId) ?? Promise.resolve();
        const result = previous.then(operation);
        const queueTail = result.then(() => undefined, () => undefined);
        this.scriptQueues.set(scriptId, queueTail);
        void queueTail.then(() => {
            if (this.scriptQueues.get(scriptId) === queueTail) this.scriptQueues.delete(scriptId);
        });
        return result;
    }
}
