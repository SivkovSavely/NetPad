export function parsePanelPayload(payloadJson?: string | null): string | null {
    if (!payloadJson) return null;

    try {
        const parsed = JSON.parse(payloadJson);
        return typeof parsed === "string" ? parsed : null;
    } catch {
        return null;
    }
}
