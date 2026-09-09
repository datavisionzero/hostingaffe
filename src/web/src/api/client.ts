import createClient, { defaultPathSerializer } from "openapi-fetch";
import type { components, paths } from "./schema";

/**
 * How a path parameter reaches the instance.
 *
 * Every one of them is a single segment and is escaped whole — a key, a slug,
 * an id — except one: a file's `path` is the last thing in its address and
 * carries the slashes of the directories it sits in (`docs/api.md`, Files). The
 * endpoint behind it is a catch-all, so `etc/caddy/Caddyfile` is three segments
 * and `etc%2Fcaddy%2FCaddyfile` is a file the instance does not have — it
 * answers `404`. The generated client cannot know that from the document, so it
 * is said here, once, rather than at every call that touches a file.
 *
 * Each segment is still escaped on its own: a space or a `#` in a file name is
 * part of the name and not part of the address.
 */
function pathSerializer(pathname: string, params: Record<string, unknown>): string {
  const { path, ...rest } = params;
  const serialized = defaultPathSerializer(pathname, rest);

  return path === undefined
    ? serialized
    : serialized.replace("{path}", String(path).split("/").map(encodeURIComponent).join("/"));
}

/**
 * The one way this application reaches the instance.
 *
 * The types are generated from `docs/api/openapi.json` before every build
 * (ADR 0005), which is what makes that document load-bearing: a route that
 * changed shape stops compiling here rather than failing on a screen. Nothing
 * hand-writes a URL or a body.
 *
 * The instance it reaches is the one that served this page. In development
 * Vite forwards what belongs to the instance, so that stays true there too.
 */
export const api = createClient<paths>({
  baseUrl: window.location.origin,
  credentials: "same-origin",
  pathSerializer,

  // Reached through `globalThis` when a request is made rather than captured
  // when this module loads, so that a test can stand an instance in front of
  // the generated client rather than in place of it.
  fetch: (request) => globalThis.fetch(request),
});

export type Schemas = components["schemas"];
export type Me = Schemas["Me"];
export type Problem = Schemas["ProblemDetails"];

/**
 * Browser requests use the opaque cookie the instance set. Unsafe requests
 * carry the application-specific CSRF proof; the browser supplies Origin and
 * the server requires both. Bearer callers remain unaffected.
 */
api.use({ onRequest({ request }) {
  if (!["GET", "HEAD", "OPTIONS"].includes(request.method) && !request.headers.has("Authorization")) {
    request.headers.set("X-Hostingaffe-CSRF", "1");
  }
  return request;
} });

/**
 * The browser session stopped working somewhere other than the screen the user is on:
 * it expired, was revoked, or the identity was deactivated. Every request answers `401` from that
 * moment, and the application has one place to notice rather than one per
 * call. The sign-in screen's own probe is the exception — there, `401` is the
 * answer to a question it asked.
 */
type SignedOutListener = () => void;

let signedOutListener: SignedOutListener | undefined;

export function whenSignedOut(listener: SignedOutListener): () => void {
  signedOutListener = listener;

  return () => {
    if (signedOutListener === listener) {
      signedOutListener = undefined;
    }
  };
}

api.use({
  onResponse({ response }) {
    if (response.status === 401 && !response.url.endsWith("/me") && !response.url.endsWith("/session")) {
      signedOutListener?.();
    }

    return response;
  },
});

/**
 * The code a client switches on: the last segment of a refusal's relative
 * `type` (`docs/api.md`, Errors). `/problems/deleted` is `deleted`.
 */
export function codeOf(problem: Problem | undefined): string | undefined {
  return problem?.type?.split("/").pop();
}

/**
 * The problem document of a refused request (RFC 9457), or a sentence when the
 * answer was not one — the instance is down, or something in between spoke.
 */
export function describe(problem: Problem | undefined, status: number): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  return `The instance answered ${status}.`;
}
