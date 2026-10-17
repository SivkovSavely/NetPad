import {BrowserWindow, dialog, ipcMain, IpcMainInvokeEvent} from "electron";
import {randomUUID} from "crypto";
import {readFile, writeFile} from "fs/promises";
import {electronConstants} from "../electron-shared";

const handles = new Map<string, {path: string; owner: number}>();
const maxHandles = 2000;

function validate(event: IpcMainInvokeEvent): number {
    const window = BrowserWindow.fromWebContents(event.sender);
    if (!window || event.senderFrame !== event.sender.mainFrame) throw new Error("Untrusted text-file IPC sender");
    let trustedOrigin = false;
    try {
        const url = new URL(event.senderFrame.url);
        trustedOrigin = url.protocol === "file:" ||
            (url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname));
    } catch {
        trustedOrigin = false;
    }
    if (!trustedOrigin) {
        throw new Error("Untrusted text-file IPC sender");
    }
    return event.sender.id;
}

function issue(path: string, owner: number) {
    const handle = randomUUID();
    handles.set(handle, {path, owner});
    while (handles.size > maxHandles) {
        const oldest = handles.keys().next().value as string | undefined;
        if (!oldest) break;
        handles.delete(oldest);
    }
    return {handle, path};
}

function resolve(handle: string, owner: number): string {
    const entry = handles.get(handle);
    if (!entry || entry.owner !== owner) throw new Error("Invalid text-file handle");
    return entry.path;
}

async function readText(path: string): Promise<string> {
    const bytes = await readFile(path);
    if (bytes.includes(0)) throw new Error("Text file contains NUL bytes");
    return new TextDecoder("utf-8", {fatal: true}).decode(bytes);
}

export class NativeTextFileHandler {
    public static init(): void {
        ipcMain.handle(electronConstants.ipcEventNames.openTextFiles, async (event, options) => {
            const owner = validate(event);
            const properties: ("openFile" | "multiSelections")[] = ["openFile"];
            if (options.multiple) properties.push("multiSelections");
            const result = await dialog.showOpenDialog(BrowserWindow.fromWebContents(event.sender)!, {
                title: options.title, filters: options.filters, defaultPath: options.defaultPath,
                properties,
            });
            if (result.canceled) return null;
            const opened = await Promise.all(result.filePaths.map(async path => ({path, text: await readText(path)})));
            return opened.map(({path, text}) => ({...issue(path, owner), text}));
        });
        ipcMain.handle(electronConstants.ipcEventNames.readTextFile, async (event, handle: string) => readText(resolve(handle, validate(event))));
        ipcMain.handle(electronConstants.ipcEventNames.saveTextFile, async (event, handle: string, text: string) => {
            await writeFile(resolve(handle, validate(event)), text, {encoding: "utf8"});
        });
        ipcMain.handle(electronConstants.ipcEventNames.saveTextFileAs, async (event, text: string, options) => {
            const owner = validate(event);
            const result = await dialog.showSaveDialog(BrowserWindow.fromWebContents(event.sender)!, {
                title: options.title, filters: options.filters, defaultPath: options.defaultPath,
            });
            if (result.canceled || !result.filePath) return null;
            await writeFile(result.filePath, text, {encoding: "utf8"});
            return issue(result.filePath, owner);
        });
    }
}
