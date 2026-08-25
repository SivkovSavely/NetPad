import {linkifySourceLocations, parseSourceLocation} from "./source-linkify";

describe("source-linkify", () => {
    function makeElement(html: string): HTMLElement {
        const el = document.createElement("div");
        el.innerHTML = html;
        return el;
    }

    it("wraps file:line references in error text with data attributes", () => {
        const el = makeElement("<div>Error at /home/user/Script.cs:42</div>");

        linkifySourceLocations(el);

        const anchor = el.querySelector("a[data-source-path]")!;
        expect(anchor).toBeTruthy();
        expect(anchor.getAttribute("data-source-path")).toBe("/home/user/Script.cs");
        expect(anchor.getAttribute("data-source-line")).toBe("42");
    });

    it("captures column when present", () => {
        const el = makeElement("<div>see src/app/Program.cs:10:5</div>");

        linkifySourceLocations(el);

        const anchor = el.querySelector("a[data-source-column]")!;
        expect(anchor.getAttribute("data-source-line")).toBe("10");
        expect(anchor.getAttribute("data-source-column")).toBe("5");
    });

    it("preserves surrounding text and multiple matches", () => {
        const el = makeElement("<div>before a.cs:1 middle b.cs:2 after</div>");

        linkifySourceLocations(el);

        expect(el.querySelectorAll("a[data-source-path]").length).toBe(2);
        expect(el.textContent).toContain("before");
        expect(el.textContent).toContain("after");
        expect(el.textContent).toContain("a.cs:1");
    });

    it("skips anchors, scripts and code elements", () => {
        const el = makeElement('<div><a href="#">x.cs:1</a><script>var s = "y.cs:2";</script></div>');

        linkifySourceLocations(el);

        expect(el.querySelector("a")?.getAttribute("href")).toBe("#");
        expect(el.querySelector("script")?.textContent).toBe('var s = "y.cs:2";');
    });

    it("leaves text untouched without matches", () => {
        const el = makeElement("<div>no references here 123</div>");

        linkifySourceLocations(el);

        expect(el.querySelector("a")).toBeNull();
    });

    it("parses location data back from an anchor", () => {
        const el = makeElement('<span data-source-path="p/a.cs" data-source-line="7" data-source-column="3"></span>');
        const location = parseSourceLocation(el.firstElementChild!)!;

        expect(location.path).toBe("p/a.cs");
        expect(location.line).toBe(7);
        expect(location.column).toBe(3);
    });
});
