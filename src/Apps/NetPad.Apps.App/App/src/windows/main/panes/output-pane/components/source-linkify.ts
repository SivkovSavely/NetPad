/**
 * Converts `path:line(:col)` source-location references found in dumped error text into
 * navigable anchors carrying `data-source-path` / `data-source-line` / `data-source-column`
 * attributes. The actual navigation is performed by the dump container's
 * `onNavigateToSource` callback, which the host wires to session/document-opening services.
 */

export const SOURCE_LOCATION_PATTERN = /((?:[A-Za-z]:)?[^\s:<>"]+\.(?:cs|netpad|ts|js|tsx|jsx)):(\d+)(?::(\d+))?/g;

const SKIP_TAGS = new Set(["a", "script", "style", "code", "pre"]);

export function linkifySourceLocations(
    root: Element,
    onNavigate?: (path: string, line?: number, column?: number) => void,
    documentObj: Document = document,
): void {
    const walker = documentObj.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
        acceptNode: node =>
            node.parentElement && SKIP_TAGS.has(node.parentElement.tagName.toLowerCase())
                ? NodeFilter.FILTER_REJECT
                : NodeFilter.FILTER_ACCEPT,
    });

    // Collect first; mutating while walking would invalidate the iterator.
    const textNodes: Text[] = [];
    let current = walker.nextNode();
    while (current) {
        textNodes.push(current as Text);
        current = walker.nextNode();
    }

    for (const textNode of textNodes) {
        const text = textNode.textContent ?? "";
        if (!text) continue;

        SOURCE_LOCATION_PATTERN.lastIndex = 0;
        if (!SOURCE_LOCATION_PATTERN.test(text)) continue;

        const fragment = documentObj.createDocumentFragment();
        let lastIndex = 0;

        SOURCE_LOCATION_PATTERN.lastIndex = 0;
        let match: RegExpExecArray | null;
        while ((match = SOURCE_LOCATION_PATTERN.exec(text)) !== null) {
            if (match.index > lastIndex) {
                fragment.appendChild(documentObj.createTextNode(text.slice(lastIndex, match.index)));
            }

            const anchor = documentObj.createElement("a");
            anchor.textContent = match[0];
            anchor.setAttribute("data-source-path", match[1]);
            anchor.setAttribute("data-source-line", match[2]);
            if (match[3]) anchor.setAttribute("data-source-column", match[3]);
            anchor.href = "javascript:void(0)";
            if (onNavigate) anchor.classList.add("source-link");

            fragment.appendChild(anchor);
            lastIndex = match.index + match[0].length;
        }

        if (lastIndex < text.length) {
            fragment.appendChild(documentObj.createTextNode(text.slice(lastIndex)));
        }

        textNode.replaceWith(fragment);
    }
}

export function parseSourceLocation(element: Element): { path: string; line?: number; column?: number } | undefined {
    const path = element.getAttribute("data-source-path");
    if (!path) return undefined;

    const lineAttr = element.getAttribute("data-source-line");
    const columnAttr = element.getAttribute("data-source-column");

    return {
        path,
        line: lineAttr ? Number(lineAttr) || undefined : undefined,
        column: columnAttr ? Number(columnAttr) || undefined : undefined,
    };
}
