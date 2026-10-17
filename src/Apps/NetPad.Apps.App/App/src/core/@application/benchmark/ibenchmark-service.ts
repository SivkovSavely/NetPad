import {DI} from "aurelia";
import {IBenchmarkApiClient} from "@application/api";

export interface IBenchmarkService extends IBenchmarkApiClient {
    cancelLatest(scriptId: string): Promise<boolean>;
}

export const IBenchmarkService = DI.createInterface<IBenchmarkService>();
