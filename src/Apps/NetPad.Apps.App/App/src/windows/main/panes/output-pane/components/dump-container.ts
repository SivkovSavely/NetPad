import {ScriptOutput, Settings} from "@application";
import {DisposableCollection, IDisposable, KeyCode, Util} from "@common";
import {ResultControls} from "./result-controls";
import {NavigationControls} from "./navigation-controls";
import {linkifySourceLocations, parseSourceLocation} from "./source-linkify";
import "highlight.js/styles/monokai.min.css";
import "katex/dist/katex.min.css";
import {UiUtil} from "@common/utils/ui-util";

export class DumpContainer implements IDisposable {
    public readonly element: HTMLElement;
    public navigationControls: NavigationControls;
    public resultControls: ResultControls;
    public scrollOnOutput = false;
    public textWrap = false;
    public lastOutputOrder = 0;             // The order of the last output message rendered

    // Invoked when the user clicks an on-demand placeholder (Util.OnDemand) rendered in this container
    public onExpandOnDemand?: (outputId: string) => void;

    // Invoked when the user clicks a Hyperlinq action link rendered in this container
    public onInvokeAction?: (actionId: string) => void;

    // Invoked when the user clicks a source location link in dumped error output
    public onNavigateToSource?: (path: string, line?: number, column?: number) => void;

    private renderQueue: Element[] = [];
    private lastRenderedOutput?: Element | null;

    // A queue to temporarily park output messages that should not be rendered yet because
    // they are not in the correct order and should wait to be rendered until previously emitted
    // messages arrive.
    private earlyMessagesOutputQueue: ScriptOutput[] = [];
    private scrollTop = 0;
    private disposables = new DisposableCollection();

    // While auto-scroll is enabled, manual scrolling away from the bottom pauses following
    // until the user scrolls back near the bottom.
    private followPaused = false;

    private scrollListenerAttachedTo?: HTMLElement;

    // Binding scopes created for mutable outputs, keyed by output id. When a mutable output is
    // replaced, the scope of the slot it replaces is disposed so its resources do not accumulate
    // for the lifetime of the container.
    private bindingScopes = new Map<string, IDisposable>();
    // The binding scope created by the most recent beforeAppendHtml() call.
    private lastBindingScope?: IDisposable;

    constructor(settings: Settings) {
        this.element = document.createElement("div");
        this.element.classList.add("dump-container");
        this.element.tabIndex = 0;
        this.disposables.add(() => this.element.remove());

        this.textWrap = settings.results.textWrap;

        this.navigationControls = new NavigationControls(this.element);

        this.resultControls = new ResultControls(this.element);
        this.disposables.add(() => this.resultControls.dispose());

        const token = UiUtil.confineSelectAllToElement(this.element);
        this.disposables.add(token);

        const onClick = (ev: MouseEvent) => {
            const target = ev.target instanceof Element ? ev.target.closest("[data-on-demand-id]") : null;
            if (target) {
                this.onExpandOnDemand?.(target.getAttribute("data-on-demand-id")!);
                return;
            }

            const actionLink = ev.target instanceof Element ? ev.target.closest("[data-netpad-action-id]") : null;
            if (actionLink) {
                this.onInvokeAction?.(actionLink.getAttribute("data-netpad-action-id")!);
                return;
            }

            const sourceLink = ev.target instanceof Element ? ev.target.closest("[data-source-path]") : null;
            if (sourceLink) {
                const location = parseSourceLocation(sourceLink);
                if (location) {
                    this.onNavigateToSource?.(location.path, location.line, location.column);
                }
            }
        };
        this.element.addEventListener("click", onClick);
        this.disposables.add(() => this.element.removeEventListener("click", onClick));
    }


    public attachedToDom() {
        if (this.element.parentElement) {
            this.element.parentElement.scrollTop = this.scrollTop;
            this.attachScrollListener(this.element.parentElement as HTMLElement);
        }
    }

    public detachingFromDom() {
        this.scrollTop = this.element.parentElement?.scrollTop ?? 0;
        this.detachScrollListener();
    }

    private attachScrollListener(scroller: HTMLElement) {
        if (this.scrollListenerAttachedTo === scroller) return;

        this.detachScrollListener();

        scroller.addEventListener("scroll", this.onScrollerScroll);
        this.scrollListenerAttachedTo = scroller;
    }

    private detachScrollListener() {
        this.scrollListenerAttachedTo?.removeEventListener("scroll", this.onScrollerScroll);
        this.scrollListenerAttachedTo = undefined;
    }

    private onScrollerScroll = () => {
        if (!this.scrollOnOutput) {
            this.followPaused = false;
            return;
        }

        // Pause following while the user scrolls away from the bottom; resume when they return.
        this.followPaused = !this.isNearBottom();
    };

    private isNearBottom(): boolean {
        const scroller = this.scrollListenerAttachedTo ?? this.element.parentElement;
        if (!scroller) return true;

        return scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 40;
    }

    public getHtml() {
        return this.element.innerHTML;
    }

    public setHtml(html: string) {
        this.clearOutput(true);
        this.appendHtml(html);
    }

    public appendOutput(output: ScriptOutput) {
        // If output does not have an order
        if (!output.order || output.order <= 0) {
            this.renderOutput(output);
            return;
        }

        // If output order is not the next one that should be outputted, add it to the pending queue
        if (output.order > 0 && output.order > (this.lastOutputOrder + 1)) {
            this.earlyMessagesOutputQueue.push(output);
            return;
        }

        // Append the output
        this.lastOutputOrder = output.order;
        this.renderOutput(output);

        // Append any "early" outputs that come after this output
        let earlyOutputIx: number;
        do {
            earlyOutputIx = this.earlyMessagesOutputQueue.findIndex(o => o.order === this.lastOutputOrder + 1);
            if (earlyOutputIx >= 0) {
                const pendingOutput = this.earlyMessagesOutputQueue[earlyOutputIx];

                this.lastOutputOrder = pendingOutput.order;
                this.renderOutput(pendingOutput);

                this.earlyMessagesOutputQueue.splice(earlyOutputIx, 1);
            }
        }
        while (earlyOutputIx >= 0);
    }

    private renderOutput(output: ScriptOutput) {
        if (output.isUpdate && output.outputId) {
            this.replaceOutput(output.outputId, output.body);
        } else {
            this.appendHtml(output.body, output.outputId);
        }
    }

    protected mutateHtmlBeforeAppend(html: string) {
        return html;
    }

    protected beforeAppendHtml(documentFragment: DocumentFragment) {
        this.lastBindingScope = this.resultControls.bind(documentFragment);
    }

    private takeLastBindingScope() {
        const scope = this.lastBindingScope;
        this.lastBindingScope = undefined;
        return scope;
    }

    private swapBindingScope(outputId: string, scope?: IDisposable) {
        const oldScope = this.bindingScopes.get(outputId);
        if (oldScope) {
            // The replaced scope is still owned by resultControls for whole-container cleanup;
            // release it from there so it does not accumulate for the container's lifetime.
            this.resultControls.removeDisposable(oldScope);
            oldScope.dispose();
        }
        if (scope) {
            this.bindingScopes.set(outputId, scope);
        } else {
            this.bindingScopes.delete(outputId);
        }
    }

    private appendHtml(html: string | null | undefined, outputId?: string) {
        if (!html) {
            return;
        }

        html = this.mutateHtmlBeforeAppend(html);

        const template = document.createElement("template");
        template.innerHTML = html;

        this.lastBindingScope = undefined;
        this.beforeAppendHtml(template.content);
        const scope = this.takeLastBindingScope();

        const children = Array.from(template.content.children);
        if (!children.length)
            throw new Error("Empty DocumentFragment");

        if (outputId) {
            children[0].setAttribute("data-output-id", outputId);
            if (scope) this.bindingScopes.set(outputId, scope);
        }

        let htmlToAppendToLastRenderedOutput: string = "";

        for (const child of children) {
            if (!outputId && this.lastRenderedOutput && this.shouldAppendOutputChildToLastRenderedOutput(child, this.lastRenderedOutput)) {
                const childHtml = child.innerHTML;
                htmlToAppendToLastRenderedOutput += childHtml;
            } else {
                this.lastRenderedOutput = child;
                this.renderQueue.push(child);
            }
        }

        if (htmlToAppendToLastRenderedOutput) {
            this.lastRenderedOutput!.innerHTML = this.lastRenderedOutput!.innerHTML + htmlToAppendToLastRenderedOutput;

            if (this.scrollOnOutput && !this.followPaused) {
                this.navigationControls.navigateBottom();
            }

            this.afterAppendHtml();
        }

        this.processRenderQueue();
    }

    private replaceOutput(outputId: string, html: string | null | undefined) {
        const renderedTarget = Array.from(this.element.children)
            .find(element => element.getAttribute("data-output-id") === outputId);
        if (renderedTarget) {
            if (!html) return;

            const replacement = this.createOutputChildren(html, outputId);
            if (!replacement.children.length) return;

            renderedTarget.replaceWith(...replacement.children);
            if (this.lastRenderedOutput === renderedTarget) {
                this.lastRenderedOutput = replacement.children[replacement.children.length - 1];
            }
            this.swapBindingScope(outputId, replacement.scope);
            this.postProcessRenderedElements(replacement.children);
            this.afterRenderedOutput();
            return;
        }

        const pendingIx = this.renderQueue.findIndex(element => element.getAttribute("data-output-id") === outputId);
        if (pendingIx < 0 || !html) return;

        const target = this.renderQueue[pendingIx];
        const replacement = this.createOutputChildren(html, outputId);
        if (!replacement.children.length) return;

        this.renderQueue.splice(pendingIx, 1, ...replacement.children);
        if (this.lastRenderedOutput === target) {
            this.lastRenderedOutput = replacement.children[replacement.children.length - 1];
        }
        this.swapBindingScope(outputId, replacement.scope);
    }

    private createOutputChildren(html: string, outputId?: string) {
        html = this.mutateHtmlBeforeAppend(html);
        const template = document.createElement("template");
        template.innerHTML = html;
        this.lastBindingScope = undefined;
        this.beforeAppendHtml(template.content);
        const scope = this.takeLastBindingScope();

        const children = Array.from(template.content.children);
        if (!children.length) {
            // Nothing was rendered from the candidate, so discard any resources created for it.
            scope?.dispose();
            return {children, scope: undefined};
        }

        if (outputId) {
            children[0].setAttribute("data-output-id", outputId);
        }
        return {children, scope};
    }

    private postProcessRenderedElements(elements: Element[]) {
        for (const group of elements) {
            const markdownEl = group.querySelector(":scope > .netpad-markdown");
            if (markdownEl) {
                this.renderMarkdown(markdownEl);
                continue;
            }

            const latexEl = group.querySelector(":scope > .netpad-latex");
            if (latexEl) {
                this.renderLatex(latexEl);
                continue;
            }

            if (group.classList.contains("code")) {
                const codeEl = group.querySelector("code");
                if (codeEl) {
                    const lang = codeEl.getAttribute("language");
                    const code = codeEl.textContent ?? "";

                    if (code) {
                        import("highlight.js/lib/common") // remove to a prop
                            .then(m => m.default)
                            .then(hljs => {
                                codeEl.innerHTML = !lang || lang === "auto" || !hljs.autoDetection(lang)
                                    ? hljs.highlightAuto(code).value
                                    : hljs.highlight(code, {language: lang}).value;
                            });
                    }
                }
            } else if (group.classList.contains("error")) {
                const first = group.childNodes[0];
                const firstTextContent = first?.textContent
                if (firstTextContent) {
                    const ix = firstTextContent.indexOf(":");
                    if (ix >= 0 && ix < firstTextContent.length - 1) {
                        const partOne = firstTextContent.substring(0, ix);
                        const partTwo = firstTextContent.substring(ix);

                        const spanOne = document.createElement("span");
                        spanOne.textContent = partOne;
                        spanOne.classList.add("error-title");

                        const spanTwo = document.createTextNode(partTwo);

                        first.replaceWith(spanOne, spanTwo);
                    }
                }

                // Make file:line references in error output navigable when a navigation
                // callback is wired by the host.
                if (this.onNavigateToSource) {
                    linkifySourceLocations(group, this.onNavigateToSource);
                }
            } else if (group.lastElementChild?.tagName.toLowerCase() === "script") {
                // Script tags cannot be injected as is, they must be recreated and appended to the DOM for
                // them to execute.
                const script = document.createElement("script");
                const code = document.createTextNode(group.textContent ?? "");
                script.appendChild(code);
                // Replace the previous script
                group.lastElementChild.remove();
                group.appendChild(script);
            }
        }
    }

    private renderMarkdown(el: Element) {
        const text = el.textContent ?? "";

        import("marked")
            .then(m => m.marked)
            .then(marked => {
                let source = text;

                // Raw HTML is escaped by default so untrusted markdown cannot inject markup.
                if (el.getAttribute("data-allow-raw-html") !== "true") {
                    source = source
                        .replace(/&/g, "&amp;")
                        .replace(/</g, "&lt;")
                        .replace(/>/g, "&gt;");
                }

                el.innerHTML = marked.parse(source, {async: false}) as string;
            })
            .catch(err => console.error("Failed to render markdown", err));
    }

    private renderLatex(el: Element) {
        const text = el.textContent ?? "";
        const displayMode = el.getAttribute("data-display-mode") === "true";

        import("katex")
            .then(m => m.default)
            .then(katex => {
                katex.render(text, el as HTMLElement, {displayMode, throwOnError: false});
            })
            .catch(err => console.error("Failed to render LaTeX", err));
    }

    private afterRenderedOutput() {
        if (this.scrollOnOutput && !this.followPaused) {
            this.navigationControls.navigateBottom();
        }
        this.afterAppendHtml();
    }

    protected afterAppendHtml() {
    }

    private processRenderQueue = Util.debounce(this, () => {
        const batch = [...this.renderQueue.splice(0)];

        this.postProcessRenderedElements(batch);

        if (batch.length === 0) {
            return;
        }

        this.element.append(...batch);

        this.afterRenderedOutput();
    }, 5);

    protected beforeClearOutput() {
        this.resultControls.dispose();
    }

    /**
     * Clears output.
     * @param reset set to true if output order should be reset;
     * if we're clearing in preparation for a new list/group of outputs
     */
    public clearOutput(reset = false) {
        this.beforeClearOutput();
        this.lastRenderedOutput = null;
        this.renderQueue.splice(0);
        this.bindingScopes.clear();
        this.element.innerHTML = "";

        if (reset) {
            this.lastOutputOrder = 0;
            this.earlyMessagesOutputQueue.splice(0);
            this.followPaused = false;
        }
    }

    private shouldAppendOutputChildToLastRenderedOutput(child: Element, lastRenderedOutput: Element) {
        if (!lastRenderedOutput
            || lastRenderedOutput.hasAttribute("data-output-id")
            || child.classList.contains("titled")) return false;

        const lastOutputIsInlinableGroupText = lastRenderedOutput.classList.contains("group")
            && lastRenderedOutput.classList.contains("text")
            && lastRenderedOutput.lastElementChild?.tagName.toLowerCase() !== "br";

        const childIsGroupText = child.classList.contains("group") && child.classList.contains("text");

        if (!(lastOutputIsInlinableGroupText && childIsGroupText)) return false;

        if ((lastRenderedOutput.classList.contains("error") && !child.classList.contains("error"))
            || (!lastRenderedOutput.classList.contains("error") && child.classList.contains("error"))) {
            return false;
        }

        return true;
    }

    public dispose() {
        this.disposables.dispose();
    }
}
