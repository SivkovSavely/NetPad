import {ApiException, CodePaneContent} from "@application/api";
import {ProblemDetails} from "@application/api-problem-details";

const fallbackMessage = "Could not load native JIT disassembly. Check logs for more info.";

export function getDisassemblyErrorMessage(error: unknown): string {
    if (error instanceof ApiException) {
        return errorMessageFrom(error.result)
            || errorMessageFrom(error.response)
            || fallbackMessage;
    }

    return errorMessageFrom(error) || fallbackMessage;
}

function errorMessageFrom(value: unknown): string | undefined {
    if (typeof value === "string") {
        try {
            return errorMessageFrom(JSON.parse(value));
        } catch {
            return undefined;
        }
    }

    if (!value || typeof value !== "object") return;

    const codePaneError = CodePaneContent.fromJS(value).error;
    if (codePaneError) return codePaneError;

    const problemDetail = ProblemDetails.fromJS(value).detail;
    if (problemDetail) return problemDetail;

    const result = (value as {result?: unknown}).result;
    return result === undefined ? undefined : errorMessageFrom(result);
}
