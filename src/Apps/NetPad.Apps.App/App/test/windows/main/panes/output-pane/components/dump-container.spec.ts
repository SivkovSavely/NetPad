import {Settings, ScriptOutput} from "@application";
import {DumpContainer} from "../../../../../../src/windows/main/panes/output-pane/components/dump-container";

function createContainer() {
    return new DumpContainer({results: {textWrap: false}} as Settings);
}

function output(order: number, body: string, data: Partial<ScriptOutput> = {}): ScriptOutput {
    return {kind: "Result", order, body, format: "Html", isUpdate: false, ...data} as ScriptOutput;
}

describe("DumpContainer mutable output", () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    test("keeps updates at their original position and preserves the merge boundary", () => {
        const container = createContainer();
        container.appendOutput(output(1, '<div class="group text">before</div>'));
        container.appendOutput(output(2, '<div class="group text">initial</div>', {outputId: "mutable"}));
        container.appendOutput(output(3, '<div class="group text">after</div>'));
        jest.advanceTimersByTime(5);

        container.appendOutput(output(4, '<div class="group text">updated</div>', {outputId: "mutable", isUpdate: true}));

        expect(container.element.textContent).toContain("before");
        expect(container.element.textContent).toContain("updated");
        expect(container.element.textContent).toContain("after");
        expect(Array.from(container.element.children).map(e => e.textContent)).toEqual(["before", "updated", "after"]);
    });

    test("replaces pending output with only the latest update", () => {
        const container = createContainer();
        container.appendOutput(output(1, '<div class="group text">initial</div>', {outputId: "mutable"}));
        container.appendOutput(output(2, '<div class="group text">middle</div>', {outputId: "mutable", isUpdate: true}));
        container.appendOutput(output(3, '<div class="group text">latest</div>', {outputId: "mutable", isUpdate: true}));
        jest.advanceTimersByTime(5);

        expect(container.element.children).toHaveLength(1);
        expect(container.element.firstElementChild?.textContent).toBe("latest");
    });

    test("keeps an unordered update in the position of the slot it replaces", () => {
        const container = createContainer();
        container.appendOutput(output(1, '<div class="group text">before</div>'));
        container.appendOutput(output(2, '<div class="group text">initial</div>', {outputId: "mutable"}));
        container.appendOutput(output(3, '<div class="group text">after</div>'));
        jest.advanceTimersByTime(5);

        container.appendOutput(output(0, '<div class="group text">updated</div>', {outputId: "mutable", isUpdate: true}));

        expect(Array.from(container.element.children).map(e => e.textContent)).toEqual(["before", "updated", "after"]);
    });

    test("retains only live non-empty binding scopes in the whole-container registry", () => {
        const container = createContainer();
        const addScopeSpy = jest.spyOn(container.resultControls, "addDisposable");
        const removeScopeSpy = jest.spyOn(container.resultControls, "removeDisposable");

        const titledTable = (label: string) =>
            `<div class="group titled"><div class="title">${label}</div>` +
            "<table><thead><tr><th>heading</th></tr></thead><tbody><tr><td>cell</td></tr></tbody></table></div>";

        // A plain output creates an empty scope that must not be retained.
        container.appendOutput(output(1, '<div class="group text">plain</div>'));
        expect(addScopeSpy).not.toHaveBeenCalled();

        container.appendOutput(output(2, titledTable("initial"), {outputId: "mutable"}));
        jest.advanceTimersByTime(5);
        expect(addScopeSpy).toHaveBeenCalledTimes(1);
        const initialScope = addScopeSpy.mock.calls[0][0];

        container.appendOutput(output(3, titledTable("updated"), {outputId: "mutable", isUpdate: true}));

        // The replaced slot's scope was released from whole-container ownership...
        expect(removeScopeSpy).toHaveBeenCalledTimes(1);
        expect(removeScopeSpy).toHaveBeenCalledWith(initialScope);
        // ...and only the replacement's scope was registered in its place.
        expect(addScopeSpy).toHaveBeenCalledTimes(2);
        expect(addScopeSpy.mock.calls[1][0]).not.toBe(initialScope);

        // An update that renders nothing retains nothing.
        container.appendOutput(output(4, " ", {outputId: "mutable", isUpdate: true}));
        expect(addScopeSpy).toHaveBeenCalledTimes(2);
        expect(removeScopeSpy).toHaveBeenCalledTimes(1);

        addScopeSpy.mockRestore();
        removeScopeSpy.mockRestore();
    });

    test("swaps interactive resources when a mutable slot is replaced and keeps no-ops intact", () => {
        const container = createContainer();
        const addSpy = jest.spyOn(document, "addEventListener");
        const removeSpy = jest.spyOn(document, "removeEventListener");

        const docHandlers = (spy: jest.SpyInstance, type: string) =>
            spy.mock.calls.filter(call => call[0] === type).map(call => call[1]);

        const titledTable = (label: string) =>
            `<div class="group titled"><div class="title">${label}</div>` +
            "<table><thead><tr><th>heading</th></tr></thead><tbody><tr><td>cell</td></tr></tbody></table></div>";

        container.appendOutput(output(1, '<div class="group text">before</div>'));
        container.appendOutput(output(2, titledTable("initial"), {outputId: "mutable"}));
        jest.advanceTimersByTime(5);

        const firstMoveHandlers = docHandlers(addSpy, "mousemove");
        const firstUpHandlers = docHandlers(addSpy, "mouseup");
        expect(firstMoveHandlers.length).toBeGreaterThan(0);

        container.appendOutput(output(3, '<div class="group text">after</div>'));
        container.appendOutput(output(4, titledTable("updated"), {outputId: "mutable", isUpdate: true}));
        jest.advanceTimersByTime(5);

        // The replaced slot's document-level listeners were released...
        expect(docHandlers(removeSpy, "mousemove")).toEqual(expect.arrayContaining(firstMoveHandlers));
        expect(docHandlers(removeSpy, "mouseup")).toEqual(expect.arrayContaining(firstUpHandlers));

        // ...and the replacement remains fully interactive.
        const title = container.element.querySelector(".group.titled .title")!;
        title.dispatchEvent(new MouseEvent("click"));
        expect(title.closest(".group")!.classList.contains("collapsed")).toBe(true);

        addSpy.mockClear();
        removeSpy.mockClear();

        // An update that renders nothing neither disturbs the displayed slot nor retains bindings.
        container.appendOutput(output(5, " ", {outputId: "mutable", isUpdate: true}));

        expect(docHandlers(addSpy, "mousemove")).toHaveLength(0);
        expect(docHandlers(removeSpy, "mousemove")).toHaveLength(0);
        expect(container.element.textContent).toContain("updated");

        // Clearing the container releases everything that remains bound.
        container.clearOutput(true);
        expect(docHandlers(removeSpy, "mousemove").length).toBeGreaterThan(0);

        addSpy.mockRestore();
        removeSpy.mockRestore();
    });
});

describe("DumpContainer Hyperlinq actions and rich content", () => {
    test("invokes onInvokeAction with the data-netpad-action-id of the clicked anchor", async () => {
        const container = createContainer();
        const handler = jest.fn();
        container.onInvokeAction = handler;

        container.appendOutput(output(
            1,
            '<div class="group"><span><a href="javascript:void(0)" data-netpad-action-id="hl-42">Do it</a></span></div>'
        ));

        await new Promise(resolve => setTimeout(resolve, 10)); // Render queue is debounced.

        const anchor = container.element.querySelector("[data-netpad-action-id='hl-42']")!;
        expect(anchor).not.toBeNull();

        anchor.dispatchEvent(new MouseEvent("click", {bubbles: true}));

        expect(handler).toHaveBeenCalledWith("hl-42");
    });

    test("renders markdown content and keeps embedded raw HTML escaped by default", async () => {
        jest.useRealTimers();
        const container = createContainer();
        document.body.appendChild(container.element);

        container.appendOutput(output(
            1,
            '<div class="group"><div class="netpad-markdown" data-allow-raw-html="false">**bold** &lt;script&gt;alert(1)&lt;/script&gt;</div></div>'
        ));

        // Rendering is debounced and loads the markdown parser asynchronously.
        await new Promise(resolve => setTimeout(resolve, 50));

        const markdownEl = container.element.querySelector(".netpad-markdown")!;
        expect(markdownEl.querySelector("strong")).toBeTruthy(); // Markdown was parsed.
        expect(markdownEl.querySelector("script")).toBeNull();   // Raw HTML stayed escaped.

        container.element.remove();
    });

    test("keeps the display-mode flag on LaTeX content", async () => {
        const container = createContainer();

        container.appendOutput(output(
            1,
            '<div class="group"><div class="netpad-latex" data-display-mode="true">x^2</div></div>'
        ));

        await new Promise(resolve => setTimeout(resolve, 10)); // Render queue is debounced.

        const latexEl = container.element.querySelector(".netpad-latex")!;
        expect(latexEl).not.toBeNull();
        expect(latexEl.getAttribute("data-display-mode")).toBe("true");
    });
});
