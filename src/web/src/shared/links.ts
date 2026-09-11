import type { UrlTransform } from "react-markdown";
import { installationPath, machinePath, softwarePath } from "@/record/addresses";
import { pagePath } from "@/shell/views";

/**
 * Links in a Markdown body are foreign links (planaffe ADR 0007). The library's default
 * admits `irc`, `ircs` and `xmpp` beside the three below; hostingaffe admits
 * exactly `http`, `https` and `mailto`, and a URL with any other scheme, or a
 * relative one, loses its `href` and stays text (planaffe ADR 0017).
 */
const admitted = new Set(["http:", "https:", "mailto:"]);

/**
 * The four schemes a body names another thing of the record with
 * (ADR 0007): `[Restoring a backup](page:backup-restore)`,
 * `[ex44](machine:ex44)`, `[caddy](software:caddy)`,
 * `[app-1](installation:app-1)`.
 *
 * The scheme carries the type because the address does not — a key is unique
 * per entity type and not across the instance, so the machine `caddy` and the
 * software `caddy` coexist.
 */
const record: Record<string, (address: string) => string> = {
  "machine:": machinePath,
  "software:": softwarePath,
  "installation:": installationPath,
  "page:": pagePath,
};

/**
 * The shape of a key and of a page's slug, which are the same
 * (`Key.PatternText`, `Slug.PatternText`). A scheme carrying anything else
 * names nothing, and is left to be the text it is rather than linked to an
 * address that cannot exist.
 */
const address = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/**
 * Where a link of the record leads inside this application, or nothing where
 * the target is not one of its own. The fragment is carried along: the import
 * keeps what a migrated link said about which part of a page was meant, and the
 * address it hangs off is right either way.
 */
export function recordPath(href: string | undefined): string | undefined {
  if (href === undefined) {
    return undefined;
  }

  const colon = href.indexOf(":");
  const to = colon < 0 ? undefined : record[href.slice(0, colon + 1)];

  if (to === undefined) {
    return undefined;
  }

  const rest = href.slice(colon + 1);
  const hash = rest.indexOf("#");
  const key = hash < 0 ? rest : rest.slice(0, hash);

  return address.test(key) ? to(key) + (hash < 0 ? "" : rest.slice(hash)) : undefined;
}

export const admitUrl: UrlTransform = (url) => {
  if (recordPath(url) !== undefined) {
    return url;
  }

  try {
    return admitted.has(new URL(url).protocol) ? url : undefined;
  } catch {
    return undefined;
  }
};
