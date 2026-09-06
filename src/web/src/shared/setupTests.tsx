import "@testing-library/jest-dom/vitest";
import { afterEach, vi } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(cleanup);

// jsdom lays nothing out and implements neither of these. Base UI's popups and
// the sidebar's mobile switch reach for them, and neither is a thing to assert
// about here.
Element.prototype.scrollIntoView ??= () => undefined;
Element.prototype.scrollTo ??= () => undefined;
window.matchMedia ??= (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }) as MediaQueryList;

// The jsdom vitest ships gives the page no storage. The token, the theme and
// the last project live there, so the tests get a Storage of their own.
if (typeof window.localStorage === "undefined" || window.localStorage === null) {
  const store = new Map<string, string>();
  const storage: Storage = {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key) => store.get(key) ?? null,
    key: (index) => [...store.keys()][index] ?? null,
    removeItem: (key) => {
      store.delete(key);
    },
    setItem: (key, value) => {
      store.set(key, String(value));
    },
  };
  Object.defineProperty(window, "localStorage", { value: storage, configurable: true });
}

/**
 * The Markdown editor stands in for itself here.
 *
 * `shared/Editor.tsx` is CodeMirror, which writes on a `contenteditable` and
 * needs a layout to do it — jsdom lays nothing out, has no `getClientRects` on
 * a range, and would answer every measurement with zero. Driving it here would
 * test the emptiness of jsdom rather than the application.
 *
 * So every test writes into `StandInEditor` instead: the same accessible name,
 * the same value, the same ⌘/Ctrl+Enter, and the same toolbar handle. What the
 * toolbar does is a set of pure functions over a selection
 * (`shared/markdownCommands.ts`) and is tested as such. What is not covered
 * here is CodeMirror itself — that is checked in a browser.
 */
vi.mock("@/shared/Editor", () => import("./StandInEditor"));
