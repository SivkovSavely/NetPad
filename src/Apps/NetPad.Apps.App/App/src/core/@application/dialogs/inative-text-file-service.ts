import {DI} from "aurelia";
import {FileSelectorDialogOptions} from "./inative-dialog-service";

export const INativeTextFileService = DI.createInterface<INativeTextFileService>();
export interface NativeTextFile { handle: string; path: string; text: string; source?: "library"; }
export interface NativeTextFileReference { handle: string; path: string; source?: "library"; }
export interface ITextFileContentService {
    readTextFile(handle: string): Promise<string>;
    saveTextFile(handle: string, text: string): Promise<void>;
}
export interface INativeTextFileService extends ITextFileContentService {
    openTextFiles(options: FileSelectorDialogOptions): Promise<NativeTextFile[] | null>;
    saveTextFileAs(text: string, options: FileSelectorDialogOptions): Promise<NativeTextFileReference | null>;
}
