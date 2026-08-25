import {Settings} from "@application";
import {DumpContainer} from "../../../../../../src/windows/main/panes/output-pane/components/dump-container";

function createContainer() {
    return new DumpContainer({results: {textWrap: false}} as Settings);
}

function output(order: number, body: string): any {
    return {kind: "Result", order, body, format: "Html", isUpdate: false};
}

describe("DumpContainer on-demand placeholders", () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    test("invokes onExpandOnDemand with the data-on-demand-id of the clicked anchor", () => {
        const container = createContainer();
        const handler = jest.fn();
        container.onExpandOnDemand = handler;

        container.appendOutput(output(
            1,
            '<div class="group"><span><a href="javascript:void(0)" data-on-demand-id="od-123">Load value</a></span></div>'
        ));

        jest.advanceTimersByTime(5);

        const anchor = container.element.querySelector("[data-on-demand-id='od-123']");
        expect(anchor).not.toBeNull();

        anchor!.dispatchEvent(new MouseEvent("click", {bubbles: true}));

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith("od-123");
    });

    test("does not invoke onExpandOnDemand when a non-placeholder element is clicked", () => {
        const container = createContainer();
        const handler = jest.fn();
        container.onExpandOnDemand = handler;

        container.appendOutput(output(1, '<div class="group text">plain output</div>'));
        jest.advanceTimersByTime(5);

        container.element.querySelector(".group")!.dispatchEvent(new MouseEvent("click", {bubbles: true}));

        expect(handler).not.toHaveBeenCalled();
    });
});
