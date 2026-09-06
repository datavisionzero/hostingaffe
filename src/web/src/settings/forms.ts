/** What the administration screens do with a form and a timestamp. */

export const date = (value: string) => new Date(value).toLocaleString();

/**
 * A form submitted to the instance. React empties `currentTarget` once the
 * event has been dispatched, so the form is taken here and handed to the
 * action: one that awaits and then reads it back off the event finds null, and
 * the `TypeError` that follows is reported as if the write had failed.
 */
export async function submitting(
  event: React.FormEvent<HTMLFormElement>,
  setNotice: (notice: string) => void,
  action: (data: FormData, form: HTMLFormElement) => Promise<void>,
) {
  event.preventDefault();
  const form = event.currentTarget;
  setNotice("");

  try {
    await action(new FormData(form), form);
    setNotice("Saved.");
  } catch (error) {
    setNotice(error instanceof Error ? error.message : "The instance did not answer.");
  }
}
