import {findSettingsMatches, indexSettingsControls} from "../../../src/windows/settings/settings-search";

describe("settings search", () => {
    it("indexes labels, section headings, and help text from rendered controls", () => {
        document.body.innerHTML = `
            <div class="section">
                <div class="header">Serialization</div>
                <label for="depth" title="Maximum object depth">Max Depth</label>
                <input id="depth" type="number">
                <input id="ignored" type="hidden">
            </div>`;

        const entries = indexSettingsControls(document.body, "results", "Results");
        expect(entries).toHaveLength(1);
        expect(entries[0]).toMatchObject({
            tabRoute: "results",
            tabText: "Results",
            label: "Max Depth",
            category: "Serialization",
            helpText: "Maximum object depth"
        });
    });

    it("matches all query terms against labels, help, category, and tab", () => {
        const control = document.createElement("input");
        const entries = [{
            tabRoute: "results", tabText: "Results", label: "Max Depth", controlId: "",
            category: "Serialization", helpText: "Maximum object depth", control
        }];

        expect(findSettingsMatches(entries, "maximum serialization")).toEqual(entries);
        expect(findSettingsMatches(entries, "unrelated")).toEqual([]);
        expect(findSettingsMatches(entries, "  ")).toEqual([]);
    });
});
