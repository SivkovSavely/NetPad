import {CodeDecompilationResponse} from "@application/api";

export class ShowCodeDecompilationEvent {
    constructor(public readonly scriptId: string, public readonly response: CodeDecompilationResponse) {
    }
}
