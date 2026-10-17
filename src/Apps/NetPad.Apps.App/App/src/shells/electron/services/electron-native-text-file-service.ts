import {ipcRenderer} from "electron";
import {FileSelectorDialogOptions} from "@application/dialogs/inative-dialog-service";
import {INativeTextFileService, NativeTextFile, NativeTextFileReference} from "@application/dialogs/inative-text-file-service";
import {electronConstants} from "../electron-shared";

export class ElectronNativeTextFileService implements INativeTextFileService {
    public openTextFiles(options: FileSelectorDialogOptions): Promise<NativeTextFile[] | null> {
        return ipcRenderer.invoke(electronConstants.ipcEventNames.openTextFiles, options);
    }
    public reopenTextFile(path: string): Promise<NativeTextFile> {
        return ipcRenderer.invoke(electronConstants.ipcEventNames.reopenTextFile, path);
    }
    public readTextFile(handle: string): Promise<string> {
        return ipcRenderer.invoke(electronConstants.ipcEventNames.readTextFile, handle);
    }
    public saveTextFile(handle: string, text: string): Promise<void> {
        return ipcRenderer.invoke(electronConstants.ipcEventNames.saveTextFile, handle, text);
    }
    public saveTextFileAs(text: string, options: FileSelectorDialogOptions): Promise<NativeTextFileReference | null> {
        return ipcRenderer.invoke(electronConstants.ipcEventNames.saveTextFileAs, text, options);
    }
}
