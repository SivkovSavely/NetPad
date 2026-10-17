import {DI} from "aurelia";
import type {DialogCloseResult} from "@aurelia/dialog";
import type {IAskDialogModel} from "@application/app/ask-dialog/ask-dialog";
import type {IAlertDialogModel} from "@application/app/alert-dialog/alert-dialog";

export const IFileDocumentDialogService = DI.createInterface<IFileDocumentDialogService>();

export interface IFileDocumentDialogService {
    ask(options: IAskDialogModel): Promise<DialogCloseResult>;
    alert(options: IAlertDialogModel): Promise<DialogCloseResult>;
}
