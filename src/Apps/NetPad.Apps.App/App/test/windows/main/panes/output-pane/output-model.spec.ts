import {OutputModel} from "../../../../../src/windows/main/panes/output-pane/output-model";
import {parsePanelPayload} from "../../../../../src/windows/main/panes/output-pane/panel-command";

describe("OutputModel panels", () => {
    it("applies callbacks when a panel is first created", () => {
        const model = new OutputModel(null as any, {results: {textWrap: false}} as any);
        const invoked: string[] = [];

        const panel = model.getOrCreatePanel("Panel", container => {
            container.onInvokeAction = id => invoked.push(id);
        });

        panel.onInvokeAction!("action-1");

        expect(invoked).toEqual(["action-1"]);
    });

    it("parses JSON panel command payloads", () => {
        expect(parsePanelPayload(JSON.stringify("Panel A"))).toBe("Panel A");
        expect(parsePanelPayload("Panel A")).toBeNull();
    });
});
