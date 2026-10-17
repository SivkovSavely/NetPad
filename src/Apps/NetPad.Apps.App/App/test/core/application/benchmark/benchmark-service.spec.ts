import {ApiException, BenchmarkApiClient} from "@application/api";
import {BenchmarkService} from "@application/benchmark/benchmark-service";

describe("BenchmarkService", () => {
    afterEach(() => {
        jest.restoreAllMocks();
    });

    it("cancels the previous operation before tracking the next one", async () => {
        const start = jest.spyOn(BenchmarkApiClient.prototype, "start")
            .mockResolvedValueOnce("operation-1")
            .mockResolvedValueOnce("operation-2");
        const cancel = jest.spyOn(BenchmarkApiClient.prototype, "cancel").mockResolvedValue(null);
        const service = new BenchmarkService();

        await service.start("script-1", "first selection");
        await service.start("script-1", "second selection");

        expect(cancel).toHaveBeenCalledWith("script-1", "operation-1");
        expect(cancel.mock.invocationCallOrder[0]).toBeLessThan(start.mock.invocationCallOrder[1]);
        expect(await service.cancelLatest("script-1")).toBe(true);
        expect(await service.cancelLatest("script-1")).toBe(false);
        expect(start).toHaveBeenCalledTimes(2);
    });

    it("clears stale operation ids when cancellation returns 404", async () => {
        jest.spyOn(BenchmarkApiClient.prototype, "start")
            .mockResolvedValueOnce("operation-1")
            .mockResolvedValueOnce("operation-2");
        const cancel = jest.spyOn(BenchmarkApiClient.prototype, "cancel")
            .mockRejectedValueOnce(new ApiException("not found", 404, "", {}, null))
            .mockResolvedValue(null);
        const service = new BenchmarkService();

        await service.start("script-1", "first selection");
        await expect(service.start("script-1", "second selection")).resolves.toBe("operation-2");

        expect(cancel).toHaveBeenCalledWith("script-1", "operation-1");
        expect(await service.cancelLatest("script-1")).toBe(true);
        expect(cancel).toHaveBeenLastCalledWith("script-1", "operation-2");
        expect(await service.cancelLatest("script-1")).toBe(false);
    });

    it("retains an operation when cancellation fails so it can be retried", async () => {
        jest.spyOn(BenchmarkApiClient.prototype, "start").mockResolvedValue("operation-1");
        const cancel = jest.spyOn(BenchmarkApiClient.prototype, "cancel")
            .mockRejectedValueOnce(new Error("cancel failed"))
            .mockResolvedValue(null);
        const service = new BenchmarkService();
        await service.start("script-1", "selection");

        await expect(service.cancelLatest("script-1")).rejects.toThrow("cancel failed");
        expect(await service.cancelLatest("script-1")).toBe(true);
        expect(cancel).toHaveBeenCalledTimes(2);
    });
});
