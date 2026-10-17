import {renderChart} from "../../../../../../src/windows/main/panes/output-pane/components/chart-view";

describe("result chart renderer", () => {
    test.each([
        ["line", [{label: "a", x: 0, y: 1}, {label: "b", x: 1, y: 3}], "path"],
        ["bar", [{label: "a", x: 0, y: 1}], "rect"],
        ["column", [{label: "a", x: 0, y: 1}], "rect"],
        ["scatter", [{label: "a", x: 1, y: 2}], "circle"],
        ["pie", [{label: "a", x: 0, y: 2}, {label: "b", x: 0, y: 1}], "path"]
    ] as const)("renders a responsive %s chart as SVG", (type, points, mark) => {
        const root = document.createElement("div");
        root.setAttribute("data-chart-model", JSON.stringify({type, title: "Data", points}));

        renderChart(root);

        const svg = root.querySelector("svg.netpad-chart-svg")!;
        expect(svg.getAttribute("viewBox")).toBe("0 0 640 340");
        expect(svg.getAttribute("style")).toContain("width:100%");
        expect(svg.querySelector(mark)).not.toBeNull();
    });

    test("leaves fallback content untouched when the model is invalid", () => {
        const root = document.createElement("div");
        root.setAttribute("data-chart-model", "bad json");
        root.innerHTML = "<table>fallback</table>";

        renderChart(root);

        expect(root.querySelector("table")?.textContent).toBe("fallback");
    });
});
