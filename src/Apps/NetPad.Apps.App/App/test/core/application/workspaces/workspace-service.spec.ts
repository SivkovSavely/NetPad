import {IHttpClient} from "@aurelia/fetch-client";
import {WorkspaceService} from "@application/workspaces/workspace-service";

describe("WorkspaceService", () => {
    test("normalizes a missing document to the implicit default workspace", async () => {
        const http = {fetch: jest.fn().mockResolvedValue({ok: true, json: async () => ({version: 1})})};
        const service = new WorkspaceService(http as unknown as IHttpClient);

        await service.initialize();

        expect(service.manifest.isInitialized).toBe(false);
        expect(service.manifest.activeWorkspaceId).toBe("default");
        expect(service.manifest.workspaces).toEqual([
            {id: "default", name: "Default", documents: [], activeHostName: "main"},
        ]);
    });

    test("saves the versioned manifest and marks workspace migration complete", async () => {
        const http = {fetch: jest.fn().mockResolvedValue({ok: true, json: async () => ({
            version: 1,
            isInitialized: false,
            activeWorkspaceId: "default",
            workspaces: [{id: "default", name: "Default", documents: [], activeHostName: "main"}],
            shelves: [],
        })})};
        const service = new WorkspaceService(http as unknown as IHttpClient);
        await service.initialize();

        await service.save(true);

        expect(service.manifest.version).toBe(1);
        expect(service.manifest.isInitialized).toBe(true);
        expect(http.fetch).toHaveBeenLastCalledWith("/workspaces", expect.objectContaining({
            method: "PUT",
            keepalive: true,
        }));
    });
});
