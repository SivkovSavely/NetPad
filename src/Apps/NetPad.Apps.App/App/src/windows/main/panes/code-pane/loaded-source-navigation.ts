import type {LibraryTextFileService} from "@application/file-search/library-text-file-service";
import type {IWorkAreaService} from "../../work-area/work-area-service";

export async function tryOpenLoadedSource(
    path: string,
    lineNumber: number,
    fileService: Pick<LibraryTextFileService, "openTextFile">,
    workAreaService: Pick<IWorkAreaService, "openFile">,
    canOpen: () => boolean): Promise<boolean> {
    try {
        const file = await fileService.openTextFile(path);
        if (!canOpen()) return true;
        await workAreaService.openFile(file, lineNumber);
        return true;
    } catch {
        return false;
    }
}
