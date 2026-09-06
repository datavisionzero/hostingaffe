import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { expect, it, vi } from "vitest";
import { LabelPicker, type PickableLabel } from "./label-picker";

const known: PickableLabel[] = [
  { name: "bug", group: "kind", description: "Something is wrong" },
  { name: "chore", group: "kind", description: "Neither a bug nor a feature" },
  { name: "web", group: null, description: "Browser application" },
];

function Harness({
  initial = [],
  onCreate,
}: {
  initial?: string[];
  onCreate?: (name: string) => Promise<PickableLabel>;
}) {
  const [value, setValue] = useState(initial);

  return (
    <>
      <LabelPicker label="Labels" labels={known} value={value} onChange={setValue} onCreate={onCreate} />
      <p data-testid="chosen">{value.join(" ")}</p>
    </>
  );
}

it("suggests the project's labels by group, with what each one means", async () => {
  render(<Harness />);
  const user = userEvent.setup();

  await user.click(screen.getByRole("combobox", { name: "Labels" }));

  expect(screen.getByText("kind · one of")).toBeInTheDocument();
  expect(screen.getByText("Ungrouped")).toBeInTheDocument();
  expect(screen.getByText("Something is wrong")).toBeInTheDocument();
  // Grouped first, ungrouped after them.
  expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
    "bugSomething is wrong",
    "choreNeither a bug nor a feature",
    "webBrowser application",
  ]);
});

it("chooses with the arrow keys and Enter, and takes the last one back on Backspace", async () => {
  render(<Harness />);
  const user = userEvent.setup();
  const field = screen.getByRole("combobox", { name: "Labels" });

  await user.click(field);
  await user.keyboard("{ArrowDown}{Enter}");
  expect(screen.getByTestId("chosen")).toHaveTextContent("chore");

  await user.keyboard("{Backspace}");
  expect(screen.getByTestId("chosen")).toHaveTextContent("");
});

it("replaces the sibling of a group instead of running into the refusal", async () => {
  render(<Harness initial={["bug"]} />);
  const user = userEvent.setup();

  await user.type(screen.getByRole("combobox", { name: "Labels" }), "chore");
  expect(screen.getByText("replaces bug")).toBeInTheDocument();

  await user.keyboard("{Enter}");
  expect(screen.getByTestId("chosen")).toHaveTextContent("chore");
});

it("offers a name nobody has yet as one to create, and attaches it", async () => {
  const create = vi.fn(async (name: string) => ({ name, group: null, description: null }));
  render(<Harness onCreate={create} />);
  const user = userEvent.setup();

  await user.type(screen.getByRole("combobox", { name: "Labels" }), "spike");
  await user.click(screen.getByRole("option", { name: "Create spike" }));

  expect(create).toHaveBeenCalledWith("spike");
  expect(await screen.findByTestId("chosen")).toHaveTextContent("spike");
});

it("says why a label could not be created and keeps what was typed choosable", async () => {
  const create = vi.fn(() => Promise.reject(new Error("A label named spike already exists.")));
  render(<Harness onCreate={create} />);
  const user = userEvent.setup();

  await user.type(screen.getByRole("combobox", { name: "Labels" }), "spike{Enter}");

  expect(await screen.findByRole("alert")).toHaveTextContent("A label named spike already exists.");
  expect(screen.getByTestId("chosen")).toHaveTextContent("");
});

it("removes a chosen label from its chip", async () => {
  render(<Harness initial={["bug", "web"]} />);
  const user = userEvent.setup();

  await user.click(screen.getByRole("button", { name: "Remove bug" }));

  expect(screen.getByTestId("chosen")).toHaveTextContent("web");
});
