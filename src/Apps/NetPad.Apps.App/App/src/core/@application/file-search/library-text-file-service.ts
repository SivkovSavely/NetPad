import {IHttpClient} from "@aurelia/fetch-client";
import {ITextFileContentService, NativeTextFile} from "@application/dialogs/inative-text-file-service";

export class LibraryTextFileService implements ITextFileContentService {
    constructor(@IHttpClient private readonly http: IHttpClient) {
    }

    public async openTextFile(path: string): Promise<NativeTextFile> {
        const query = new URLSearchParams({path});
        const response = await this.http.fetch(`/search/open?${query.toString()}`);
        if (!response.ok) throw new Error(await this.errorMessage(response));
        return {...await response.json(), source: "library"} as NativeTextFile;
    }

    public async readTextFile(handle: string): Promise<string> {
        const response = await this.http.fetch(`/search/open/${encodeURIComponent(handle)}`);
        if (!response.ok) throw new Error(await this.errorMessage(response));
        return (await response.json() as {text: string}).text;
    }

    public async saveTextFile(handle: string, text: string): Promise<void> {
        const response = await this.http.fetch(`/search/open/${encodeURIComponent(handle)}`, {
            method: "PUT",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({text}),
        });
        if (!response.ok) throw new Error(await this.errorMessage(response));
    }

    private async errorMessage(response: Response): Promise<string> {
        const detail = await response.text();
        return detail || `Request failed (${response.status})`;
    }
}
