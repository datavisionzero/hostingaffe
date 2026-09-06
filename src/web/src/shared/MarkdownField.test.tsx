import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { useState } from "react";
import { MarkdownField } from "./MarkdownField";

/**
 * The one Markdown field, as the six screens that use it see it. CodeMirror
 * itself stands in as a plain text area here (`shared/setupTests.tsx`); what is
 * checked is the field around it — its name, its toolbar, its preview, and the
 * window it opens into.
 */
function Field({ label = "Description", onSubmit }: { label?: string; onSubmit?: () => void }) {
  const [value, setValue] = useState("");

  return <MarkdownField label={label} value={value} onChange={setValue} onSubmit={onSubmit} />;
}

it("carries the name of the field on the text itself", async () => {
  render(<Field />);

  // The label is not a `for` pointing at nothing: whatever holds the text
  // carries the name, whether that is the editor or what stands in for it.
  expect(await screen.findByLabelText("Description")).toBeInTheDocument();
});

it("marks up what is selected from the toolbar", async () => {
  render(<Field />);
  const user = userEvent.setup();

  const field = await screen.findByLabelText("Description");
  await user.type(field, "loud");
  (field as HTMLTextAreaElement).setSelectionRange(0, 4);

  await user.click(screen.getByRole("button", { name: "Bold" }));
  expect(field).toHaveValue("**loud**");

  await user.click(screen.getByRole("button", { name: "Quote" }));
  expect(field).toHaveValue("> **loud**");
});

it("shows the preview beside the text, and says what is in an empty one", async () => {
  render(<Field />);

  const preview = await screen.findByLabelText("Description, preview");
  expect(within(preview).getByText("Nothing to preview.")).toBeInTheDocument();

  const user = userEvent.setup();
  await user.type(await screen.findByLabelText("Description"), "A **clear** description.");

  await waitFor(() => expect(within(preview).getByText("clear")).toBeInTheDocument());
});

it("opens the same field over the window and gives it back on Done", async () => {
  render(<Field />);
  const user = userEvent.setup();

  await user.type(await screen.findByLabelText("Description"), "Half a sentence");
  await user.click(screen.getByRole("button", { name: "Full screen" }));

  // The same text, in the same field — not an empty second one.
  const dialog = await screen.findByRole("dialog");
  expect(await within(dialog).findByLabelText("Description")).toHaveValue("Half a sentence");

  await user.click(within(dialog).getByRole("button", { name: "Done" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(await screen.findByLabelText("Description")).toHaveValue("Half a sentence");
});

it("saves from inside the text, where there is something to save", async () => {
  const onSubmit = vi.fn();
  render(<Field onSubmit={onSubmit} />);
  const user = userEvent.setup();

  const field = await screen.findByLabelText("Description");
  await user.type(field, "A comment");
  await user.type(field, "{Meta>}{Enter}{/Meta}");

  expect(onSubmit).toHaveBeenCalledTimes(1);
});
