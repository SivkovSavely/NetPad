import {FeatureSearchEntry, fuzzyMatchScore, searchFeatureEntries} from "@application/feature-search/feature-search";

const entry = (label: string, extra: Partial<FeatureSearchEntry> = {}): FeatureSearchEntry => ({
    label,
    action: () => undefined,
    ...extra
});

describe("feature search", () => {
    test("matches words fuzzily across names and descriptions", () => {
        expect(fuzzyMatchScore("opn fl", entry("Open File", {description: "Browse for a script"}))).toBeDefined();
        expect(fuzzyMatchScore("browse scr", entry("Open File", {description: "Browse for a script"}))).toBeDefined();
    });

    test("matches help text and aliases", () => {
        expect(fuzzyMatchScore("ctrl z", entry("Undo", {helpText: "Ctrl + Z"}))).toBeDefined();
        expect(fuzzyMatchScore("prefs", entry("Settings", {aliases: ["preferences"]}))).toBeDefined();
    });

    test("returns most relevant entries first and excludes unmatched entries", () => {
        const entries = [entry("Open File"), entry("Open Recent"), entry("Settings")];
        expect(searchFeatureEntries(entries, "open file").map(item => item.label)).toEqual(["Open File"]);
    });

    test("returns all entries for an empty query", () => {
        const entries = [entry("One"), entry("Two")];
        expect(searchFeatureEntries(entries, "")).toEqual(entries);
    });
});
