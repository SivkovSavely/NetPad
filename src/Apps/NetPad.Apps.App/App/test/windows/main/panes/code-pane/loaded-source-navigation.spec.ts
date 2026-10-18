import {tryOpenLoadedSource} from "../../../../../src/windows/main/panes/code-pane/loaded-source-navigation";

describe("loaded source navigation", () => {
    const file = {handle: "file-1", path: "/scripts/Loaded.cs", text: "class Loaded {}", source: "library" as const};
    let openTextFile: jest.Mock;
    let openFile: jest.Mock;

    beforeEach(() => {
        openTextFile = jest.fn();
        openFile = jest.fn();
    });

    it("opens through the root-restricted file service at the symbol line", async () => {
        openTextFile.mockResolvedValue(file);

        const opened = await tryOpenLoadedSource(
            file.path,
            4,
            {openTextFile} as any,
            {openFile} as any,
            () => true);

        expect(opened).toBe(true);
        expect(openTextFile).toHaveBeenCalledWith(file.path);
        expect(openFile).toHaveBeenCalledWith(file, 4);
    });

    it("leaves paths outside configured roots for the read-only source view", async () => {
        openTextFile.mockRejectedValue(new Error("Path is outside configured roots"));

        const opened = await tryOpenLoadedSource(
            "/outside/Loaded.cs",
            1,
            {openTextFile} as any,
            {openFile} as any,
            () => true);

        expect(opened).toBe(false);
        expect(openFile).not.toHaveBeenCalled();
    });

    it("does not open source after the originating script changes", async () => {
        openTextFile.mockResolvedValue(file);

        const opened = await tryOpenLoadedSource(
            file.path,
            4,
            {openTextFile} as any,
            {openFile} as any,
            () => false);

        expect(opened).toBe(true);
        expect(openFile).not.toHaveBeenCalled();
    });
});
