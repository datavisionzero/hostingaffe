import type { Release } from "@/api/client";

/**
 * A release as Markdown — what "copy as Markdown" puts on the clipboard, and
 * the same text `ha release notes` prints, so that a release reads the same
 * wherever it is taken from: the notes, a blank line, then one bullet per
 * issue with sub-issues indented under their parent.
 *
 * The instance orders the membership parent first, then that parent's
 * sub-issues, so the indentation follows from `parent` alone and this does not
 * rebuild the tree.
 */
export function releaseMarkdown(release: Release): string {
  let text = "";

  if (release.description !== "") {
    text += `${release.description}\n`;

    if (release.issues.length > 0) {
      text += "\n";
    }
  }

  for (const issue of release.issues) {
    text += `${issue.parent === null ? "- " : "  - "}${issue.key} ${issue.title.trim()}\n`;
  }

  return text;
}
