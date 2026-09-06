import type { UrlTransform } from "react-markdown";

/**
 * Links in issue content are foreign links (ADR 0007). The library's default
 * admits `irc`, `ircs` and `xmpp` beside the three below; hostingaffe admits
 * exactly `http`, `https` and `mailto`, and a URL with any other scheme, or a
 * relative one, loses its `href` and stays text (ADR 0017).
 */
const admitted = new Set(["http:", "https:", "mailto:"]);

export const admitUrl: UrlTransform = (url) => {
  try {
    return admitted.has(new URL(url).protocol) ? url : undefined;
  } catch {
    return undefined;
  }
};

