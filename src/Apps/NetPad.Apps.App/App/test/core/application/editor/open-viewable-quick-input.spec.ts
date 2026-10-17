import {rankOpenViewables} from "@application/editor/open-viewable-quick-input";

describe("rankOpenViewables", () => {
    const picks: any[] = [
        {viewable: {id: "a", name: "Alpha Query", path: "/work/alpha"}, host: {}},
        {viewable: {id: "b", name: "Query Beta", path: "/work/beta"}, host: {}},
        {viewable: {id: "c", name: "Other", path: "/work/query.sql"}, host: {}}
    ];

    test("fuzzy matches names and paths, favoring contiguous name matches", () => {
        expect(rankOpenViewables(picks, "query", []).map(x => x.viewable.id)).toEqual(["b", "a", "c"]);
    });

    test("uses MRU order to break equal match scores", () => {
        expect(rankOpenViewables(picks, "q", ["a", "b", "c"]).map(x => x.viewable.id)).toEqual(["b", "a", "c"]);
    });

    test("returns all viewables for an empty query", () => {
        expect(rankOpenViewables(picks, "", []).map(x => x.viewable.id)).toEqual(["a", "b", "c"]);
    });
});
