import {Util} from "@common";

export function normalizeScriptRoot(path: string): string {
    return Util.trimEnd(path.replaceAll("\\", "/"), "/");
}

export function isScriptPathWithin(path: string, root: string): boolean {
    const normalizedPath = path.replaceAll("\\", "/");
    return normalizedPath === root || normalizedPath.startsWith(root + "/");
}

export function findMostSpecificScriptRoot(path: string, roots: string[]): string | undefined {
    return roots.filter(root => isScriptPathWithin(path, root))
        .sort((a, b) => b.length - a.length)[0];
}
