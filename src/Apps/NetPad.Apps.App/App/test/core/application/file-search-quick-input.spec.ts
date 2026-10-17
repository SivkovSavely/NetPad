import {buildFileSearchPicks, FileSearchMatch} from "@application/file-search/file-search-quick-input";

describe("buildFileSearchPicks", () => {
    test("groups line matches by file while retaining preview lines", () => {
        const matches: FileSearchMatch[] = [
            {path: "/scripts/a.cs", lineNumber: 2, line: "first match"},
            {path: "/scripts/a.cs", lineNumber: 9, line: "second match"},
            {path: "/scripts/b.text", lineNumber: 4, line: "other file"},
        ];

        expect(buildFileSearchPicks(matches)).toEqual([
            {type: "separator", label: "/scripts/a.cs"},
            {type: "item", label: "Line 2", description: "first match", match: matches[0]},
            {type: "item", label: "Line 9", description: "second match", match: matches[1]},
            {type: "separator", label: "/scripts/b.text"},
            {type: "item", label: "Line 4", description: "other file", match: matches[2]},
        ]);
    });
});
