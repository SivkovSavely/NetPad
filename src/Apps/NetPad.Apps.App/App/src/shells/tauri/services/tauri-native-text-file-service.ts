import {invoke} from "@tauri-apps/api/core";
import {FileSelectorDialogOptions} from "@application/dialogs/inative-dialog-service";
import {INativeTextFileService, NativeTextFile, NativeTextFileReference} from "@application/dialogs/inative-text-file-service";

export class TauriNativeTextFileService implements INativeTextFileService {
    public openTextFiles(options: FileSelectorDialogOptions): Promise<NativeTextFile[] | null> {
        return invoke<NativeTextFile[] | null>("open_text_files", {options});
    }
    public readTextFile(handle: string): Promise<string> { return invoke<string>("read_text_file", {handle}); }
    public saveTextFile(handle: string, text: string): Promise<void> { return invoke<void>("save_text_file", {handle, text}); }
    public saveTextFileAs(text: string, options: FileSelectorDialogOptions): Promise<NativeTextFileReference | null> {
        return invoke<NativeTextFileReference | null>("save_text_file_as", {text, options});
    }
}
