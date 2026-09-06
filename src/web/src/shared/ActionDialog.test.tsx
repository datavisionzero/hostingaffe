import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { Button } from "@/components/ui/button";
import { renderAt } from "@/shared/testing";
import { ActionDialog, TextActionDialog } from "./ActionDialog";

it("confirms an action in the page and returns focus to its trigger", async () => {
  const onConfirm = vi.fn().mockResolvedValue(undefined);
  renderAt("/", <ActionDialog trigger={<Button>Delete</Button>} title="Delete label?" description="The label can be restored." confirmLabel="Delete label" onConfirm={onConfirm} />);
  const user = userEvent.setup();
  const trigger = screen.getByRole("button", { name: "Delete" });

  await user.click(trigger);
  const dialog = screen.getByRole("dialog", { name: "Delete label?" });
  expect(onConfirm).not.toHaveBeenCalled();

  await user.click(within(dialog).getByRole("button", { name: "Delete label" }));

  expect(onConfirm).toHaveBeenCalledOnce();
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
});

it("edits one value in the page and can be canceled without saving", async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined);
  renderAt("/", <TextActionDialog trigger={<Button>Rename</Button>} title="Rename agent" label="Agent name" initialValue="codex" submitLabel="Save name" onSubmit={onSubmit} />);
  const user = userEvent.setup();
  const trigger = screen.getByRole("button", { name: "Rename" });

  await user.click(trigger);
  const dialog = screen.getByRole("dialog", { name: "Rename agent" });
  const input = within(dialog).getByLabelText("Agent name");
  await user.clear(input);
  await user.type(input, "browser agent");
  await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

  expect(onSubmit).not.toHaveBeenCalled();
  expect(trigger).toHaveFocus();

  await user.click(trigger);
  const reopened = screen.getByRole("dialog", { name: "Rename agent" });
  expect(within(reopened).getByLabelText("Agent name")).toHaveValue("codex");
  await user.clear(within(reopened).getByLabelText("Agent name"));
  await user.type(within(reopened).getByLabelText("Agent name"), "browser agent");
  await user.click(within(reopened).getByRole("button", { name: "Save name" }));

  expect(onSubmit).toHaveBeenCalledWith("browser agent");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
});

it("allows an optional value to be cleared", async () => {
  const onSubmit = vi.fn().mockResolvedValue(undefined);
  renderAt("/", <TextActionDialog trigger={<Button>Edit</Button>} title="Edit label" label="Description" initialValue="Temporary" required={false} submitLabel="Save label" onSubmit={onSubmit} />);
  const user = userEvent.setup();

  await user.click(screen.getByRole("button", { name: "Edit" }));
  const dialog = screen.getByRole("dialog", { name: "Edit label" });
  await user.clear(within(dialog).getByLabelText("Description"));
  await user.click(within(dialog).getByRole("button", { name: "Save label" }));

  expect(onSubmit).toHaveBeenCalledWith("");
});
