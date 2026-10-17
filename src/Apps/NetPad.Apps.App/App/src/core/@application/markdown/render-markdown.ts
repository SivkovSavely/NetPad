export async function renderMarkdown(source: string, allowRawHtml = false): Promise<string> {
    const {marked} = await import("marked");

    if (!allowRawHtml) {
        source = source
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    return marked.parse(source, {async: false}) as string;
}
