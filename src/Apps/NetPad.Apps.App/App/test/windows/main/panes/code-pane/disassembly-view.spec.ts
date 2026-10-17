import {ApiException, CodePaneContent} from "@application/api";
import {getDisassemblyErrorMessage} from "../../../../../src/windows/main/panes/code-pane/disassembly-view/disassembly-error-message";

describe("DisassemblyView", () => {
    it("uses the parsed CodePaneContent error from an API exception", () => {
        const response = new CodePaneContent({
            language: "asm",
            content: "",
            error: "Native disassembly is not supported.",
            supported: false
        });

        expect(getDisassemblyErrorMessage(new ApiException("failed", 501, "", {}, response)))
            .toBe("Native disassembly is not supported.");
    });

    it("parses a raw JSON error response", () => {
        const body = JSON.stringify({error: "The disassembler could not start."});

        expect(getDisassemblyErrorMessage(new ApiException("failed", 501, body, {}, null)))
            .toBe("The disassembler could not start.");
    });
});
