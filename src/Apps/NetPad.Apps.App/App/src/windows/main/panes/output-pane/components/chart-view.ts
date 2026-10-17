type ChartModel = {
    type: "line" | "bar" | "column" | "scatter" | "pie";
    title?: string | null;
    points: {label?: string | null; x: number; y: number}[];
};

const svgNamespace = "http://www.w3.org/2000/svg";
const palette = ["#0d6efd", "#dc3545", "#198754", "#fd7e14", "#6f42c1", "#20c997", "#d63384"];

export function renderChart(root: HTMLElement) {
    const rawModel = root.getAttribute("data-chart-model");
    if (!rawModel) return;

    let model: ChartModel;
    try {
        model = JSON.parse(rawModel) as ChartModel;
    } catch {
        return;
    }

    const svg = document.createElementNS(svgNamespace, "svg");
    svg.classList.add("netpad-chart-svg");
    svg.setAttribute("viewBox", "0 0 640 340");
    svg.setAttribute("role", "img");
    svg.setAttribute("aria-label", model.title || `${model.type} chart`);
    svg.setAttribute("style", "display:block;width:100%;height:auto;max-height:420px");

    if (model.title) {
        const title = element("text", {x: "320", y: "24", "text-anchor": "middle", class: "netpad-chart-title"});
        title.textContent = model.title;
        svg.appendChild(title);
    }

    const points = Array.isArray(model.points)
        ? model.points.filter(point => Number.isFinite(point.x) && Number.isFinite(point.y))
        : [];
    if (points.length === 0) {
        const empty = element("text", {x: "320", y: "170", "text-anchor": "middle"});
        empty.textContent = "No chart data";
        svg.appendChild(empty);
    } else if (model.type === "pie") {
        renderPie(svg, points);
    } else {
        renderCartesian(svg, points, model.type);
    }

    root.replaceChildren(svg);
}

function renderCartesian(
    svg: SVGSVGElement,
    points: ChartModel["points"],
    type: Exclude<ChartModel["type"], "pie">
) {
    const plot = {left: 54, top: 42, width: 556, height: 250};
    svg.appendChild(element("path", {
        d: `M${plot.left},${plot.top} V${plot.top + plot.height} H${plot.left + plot.width}`,
        fill: "none",
        stroke: "#8b929a",
        "stroke-width": "1"
    }));

    const minY = Math.min(0, ...points.map(point => point.y));
    const maxY = Math.max(0, ...points.map(point => point.y));
    const ySpan = maxY - minY || 1;
    const y = (value: number) => plot.top + plot.height - ((value - minY) / ySpan) * plot.height;

    if (type === "line") {
        const minX = Math.min(...points.map(point => point.x));
        const maxX = Math.max(...points.map(point => point.x));
        const xSpan = maxX - minX || 1;
        const x = (value: number) => plot.left + ((value - minX) / xSpan) * plot.width;
        const pathData = points.map((point, index) => `${index === 0 ? "M" : "L"}${x(point.x)},${y(point.y)}`).join(" ");
        svg.appendChild(element("path", {d: pathData, fill: "none", stroke: palette[0], "stroke-width": "3"}));
        for (const point of points) {
            svg.appendChild(element("circle", {cx: String(x(point.x)), cy: String(y(point.y)), r: "4", fill: palette[0]}));
        }
        return;
    }

    if (type === "scatter") {
        const minX = Math.min(...points.map(point => point.x));
        const maxX = Math.max(...points.map(point => point.x));
        const xSpan = maxX - minX || 1;
        for (const point of points) {
            svg.appendChild(element("circle", {
                cx: String(plot.left + ((point.x - minX) / xSpan) * plot.width),
                cy: String(y(point.y)),
                r: "5",
                fill: palette[0]
            }));
        }
        return;
    }

    if (type === "column") {
        const slot = plot.width / points.length;
        points.forEach((point, index) => {
            const zero = y(0);
            const valueY = y(point.y);
            svg.appendChild(element("rect", {
                x: String(plot.left + index * slot + slot * 0.15),
                y: String(Math.min(valueY, zero)),
                width: String(slot * 0.7),
                height: String(Math.max(1, Math.abs(zero - valueY))),
                fill: palette[index % palette.length]
            }));
        });
        return;
    }

    const slot = plot.height / points.length;
    points.forEach((point, index) => {
    const zero = plot.left + ((0 - minY) / ySpan) * plot.width;
    const valueX = plot.left + ((point.y - minY) / ySpan) * plot.width;
        svg.appendChild(element("rect", {
            x: String(Math.min(valueX, zero)),
            y: String(plot.top + index * slot + slot * 0.15),
            width: String(Math.max(1, Math.abs(valueX - zero))),
            height: String(slot * 0.7),
            fill: palette[index % palette.length]
        }));
        const label = element("text", {
            x: "48",
            y: String(plot.top + index * slot + slot * 0.67),
            "text-anchor": "end",
            class: "netpad-chart-label"
        });
        label.textContent = points[index].label ?? String(index + 1);
        svg.appendChild(label);
    });
}

function renderPie(svg: SVGSVGElement, points: ChartModel["points"]) {
    const positive = points.map(point => Math.max(0, point.y));
    const total = positive.reduce((sum, value) => sum + value, 0);
    if (total <= 0) return;

    let angle = -Math.PI / 2;
    const cx = 320, cy = 174, radius = 126;
    if (positive.filter(value => value > 0).length === 1) {
        svg.appendChild(element("circle", {cx: String(cx), cy: String(cy), r: String(radius), fill: palette[positive.findIndex(value => value > 0) % palette.length]}));
        return;
    }
    points.forEach((point, index) => {
        const fraction = positive[index] / total;
        if (fraction <= 0) return;
        const nextAngle = angle + fraction * Math.PI * 2;
        const start = {x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle)};
        const end = {x: cx + radius * Math.cos(nextAngle), y: cy + radius * Math.sin(nextAngle)};
        const largeArc = fraction > 0.5 ? 1 : 0;
        const path = element("path", {
            d: `M${cx},${cy} L${start.x},${start.y} A${radius},${radius} 0 ${largeArc} 1 ${end.x},${end.y} Z`,
            fill: palette[index % palette.length]
        });
        path.setAttribute("aria-label", `${point.label ?? `Item ${index + 1}`}: ${point.y}`);
        svg.appendChild(path);
        angle = nextAngle;
    });
}

function element<K extends keyof SVGElementTagNameMap>(
    tag: K,
    attributes: Record<string, string>
): SVGElementTagNameMap[K] {
    const result = document.createElementNS(svgNamespace, tag);
    for (const [name, value] of Object.entries(attributes)) result.setAttribute(name, value);
    return result;
}
