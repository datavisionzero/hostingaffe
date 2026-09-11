import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { admitUrl, recordPath } from "./links";
import { Markdown } from "./Markdown";
import { renderAt } from "./testing";

describe("the Markdown pipeline (planaffe ADR 0007)", () => {
  it("renders GitHub-flavoured Markdown to components", () => {
    render(<Markdown>{"| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n- [ ] open\n\n~~gone~~"}</Markdown>);

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
    expect(screen.getByText("gone").tagName).toBe("DEL");
  });

  // ADR 0020: the one departure from CommonMark. A person types into a text
  // area and presses Enter; nothing that reaches here is hard-wrapped, so a
  // newline is only ever meant.
  it("makes a line break of a single newline", () => {
    const { container } = render(<Markdown>{"Zeile eins\nZeile zwei\n\nAbsatz zwei"}</Markdown>);

    expect(container.querySelectorAll("br")).toHaveLength(1);
    expect(container.querySelectorAll("p")).toHaveLength(2);
  });

  it("never interprets HTML", () => {
    const { container } = render(
      <Markdown>{'before <img src="x" onerror="alert(1)"> <script>alert(1)</script> after'}</Markdown>,
    );

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).toContain("before");
    expect(container.textContent).toContain("after");
  });

  it("opens links as foreign links and admits three schemes", () => {
    render(<Markdown>{"[ok](https://example.org) [mail](mailto:a@example.org) [no](javascript:alert(1)) [rel](docs/api.md)"}</Markdown>);

    const ok = screen.getByRole("link", { name: "ok" });
    expect(ok).toHaveAttribute("href", "https://example.org");
    expect(ok).toHaveAttribute("rel", "noopener noreferrer");
    expect(ok).toHaveAttribute("target", "_blank");
    expect(screen.getByRole("link", { name: "mail" })).toHaveAttribute("href", "mailto:a@example.org");

    expect(screen.queryByRole("link", { name: "no" })).toBeNull();
    expect(screen.queryByRole("link", { name: "rel" })).toBeNull();
    expect(screen.getByText("no")).toBeInTheDocument();
  });

  it("refuses what the library would have admitted", () => {
    expect(admitUrl("irc://irc.example.org/#x", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBeUndefined();
    expect(admitUrl("xmpp:a@b", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBeUndefined();
    expect(admitUrl("http://example.org", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBe("http://example.org");
  });

  // ADR 0007: the record's own four schemes are this instance's addresses, so
  // they are followed rather than opened.
  it("follows a link of the record instead of opening it", () => {
    renderAt(
      "/pages/architecture",
      <Markdown>
        {"[the runbook](page:backup-restore) [ex44](machine:ex44) [caddy](software:caddy) [app-1](installation:app-1)"}
      </Markdown>,
    );

    const page = screen.getByRole("link", { name: "the runbook" });
    expect(page).toHaveAttribute("href", "/pages/backup-restore");
    expect(page).not.toHaveAttribute("target");
    expect(page).not.toHaveAttribute("rel");

    expect(screen.getByRole("link", { name: "ex44" })).toHaveAttribute("href", "/machines/ex44");
    expect(screen.getByRole("link", { name: "caddy" })).toHaveAttribute("href", "/software/caddy");
    expect(screen.getByRole("link", { name: "app-1" })).toHaveAttribute("href", "/installations/app-1");
  });

  it("keeps the fragment a migrated link carried", () =>
    expect(recordPath("page:backup-restore#the-volume")).toBe("/pages/backup-restore#the-volume"));

  it("leaves a scheme carrying something that is not an address as text", () => {
    render(<Markdown>{"[a](page:Architecture) [b](page:) [c](pages:setup)"}</Markdown>);

    for (const name of ["a", "b", "c"]) {
      expect(screen.queryByRole("link", { name })).toBeNull();
      expect(screen.getByText(name)).toBeInTheDocument();
    }
  });

  it("marks fenced code apart from inline code", () => {
    const { container } = render(<Markdown>{"say `ha next`\n\n```sh\nha next --claim\n```"}</Markdown>);

    expect(container.querySelector("pre code")).toHaveTextContent("ha next --claim");
    expect(container.querySelectorAll("code")).toHaveLength(2);
  });

  // ADR 0017: nothing tokenizes code here, so the word the fence named is what
  // says what the block is.
  it("names the language of a fenced block and highlights nothing", () => {
    const { container } = render(<Markdown>{"```csharp\nvar x = 1;\n```"}</Markdown>);

    expect(screen.getByText("csharp")).toBeInTheDocument();
    expect(container.querySelectorAll("pre code span")).toHaveLength(0);
  });

  it("says nothing above a fence that named nothing", () => {
    const { container } = render(<Markdown>{"```\nplain\n```"}</Markdown>);

    expect(container.querySelector("pre")).toHaveTextContent("plain");
    expect(container.querySelector("pre")?.previousElementSibling).toBeNull();
  });
});
