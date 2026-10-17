export interface SettingsSearchEntry {
    tabRoute: string;
    tabText: string;
    label: string;
    category: string;
    helpText: string;
    controlId: string;
    control: HTMLElement;
}

export function findSettingsMatches(entries: SettingsSearchEntry[], query: string): SettingsSearchEntry[] {
    const terms = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    if (!terms.length) return [];

    return entries.filter(entry => {
        const text = `${entry.label} ${entry.helpText} ${entry.category} ${entry.tabText}`.toLocaleLowerCase();
        return terms.every(term => text.includes(term));
    });
}

export function indexSettingsControls(root: HTMLElement, tabRoute: string, tabText: string): SettingsSearchEntry[] {
    const controls = root.querySelectorAll<HTMLElement>("input, select, textarea, button[role='switch']");
    return [...controls].flatMap(control => {
        if (control instanceof HTMLInputElement && ["hidden", "submit", "button", "reset"].includes(control.type))
            return [];

        const label = control.id
            ? [...root.querySelectorAll<HTMLLabelElement>("label[for]")].find(item => item.htmlFor === control.id)
            : control.closest("label");
        const text = label?.textContent?.trim() || control.getAttribute("aria-label") || "";
        if (!text) return [];

        const section = control.closest(".section");
        const category = section?.querySelector<HTMLElement>(":scope > .header")?.textContent?.trim() ?? "";
        const helpText = [control.getAttribute("title"), label?.getAttribute("title"), control.getAttribute("placeholder")]
            .filter(Boolean).join(" ");
        return [{tabRoute, tabText, label: text, category, helpText, controlId: control.id, control}];
    });
}
