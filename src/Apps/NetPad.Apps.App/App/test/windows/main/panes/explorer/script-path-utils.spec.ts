import {findMostSpecificScriptRoot, isScriptPathWithin, normalizeScriptRoot} from "../../../../../src/windows/main/panes/explorer/scripts/script-path-utils";

describe("script root path utilities", () => {
    it("normalizes roots and assigns nested paths to the most-specific root", () => {
        const roots = ["/work/scripts", "/work/scripts/shared"].map(normalizeScriptRoot);

        expect(findMostSpecificScriptRoot("/work/scripts/shared/query.sql", roots)).toBe("/work/scripts/shared");
        expect(findMostSpecificScriptRoot("/work/scripts/sharedness/query.sql", roots)).toBe("/work/scripts");
        expect(isScriptPathWithin("/work/scriptsX/query.sql", "/work/scripts")).toBe(false);
    });
});
