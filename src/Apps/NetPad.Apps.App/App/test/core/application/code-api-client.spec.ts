import {CodeApiClient} from "@application/api";

describe("CodeApiClient", () => {
    it("returns null for an empty decompilation response", async () => {
        const client = Object.create(CodeApiClient.prototype) as any;
        const response = {
            status: 204,
            headers: {forEach: jest.fn()}
        } as unknown as Response;

        await expect(client.processDecompileAtPosition(response)).resolves.toBeNull();
    });
});
