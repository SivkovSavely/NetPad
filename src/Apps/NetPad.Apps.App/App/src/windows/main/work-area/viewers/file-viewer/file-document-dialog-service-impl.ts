import {DialogUtil} from "@application/dialogs/dialog-util";
import {IFileDocumentDialogService} from "./file-document-dialog-service";

export class FileDocumentDialogService implements IFileDocumentDialogService {
    constructor(private readonly dialogUtil: DialogUtil) {
    }

    public ask(options: Parameters<IFileDocumentDialogService["ask"]>[0]): ReturnType<IFileDocumentDialogService["ask"]> {
        return this.dialogUtil.ask(options);
    }

    public alert(options: Parameters<IFileDocumentDialogService["alert"]>[0]): ReturnType<IFileDocumentDialogService["alert"]> {
        return this.dialogUtil.alert(options);
    }
}
