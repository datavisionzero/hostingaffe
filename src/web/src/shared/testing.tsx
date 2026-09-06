import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router";
import { vi } from "vitest";
import { ThemeProvider } from "@/components/theme-provider";
import { TooltipProvider } from "@/components/ui/tooltip";

/**
 * An instance to stand in front of the generated client: a route table of
 * `METHOD /path` to what it answers. Anything not listed answers 404 with a
 * problem document, the way the real one would.
 */
export type Answer = { status?: number; body?: unknown } | Record<string, unknown> | unknown[];
export type Route = Answer | ((request: Request) => Answer);

export function installInstance(routes: Record<string, Route>) {
  const calls: Request[] = [];

  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(input, init);
    calls.push(request);

    const url = new URL(request.url);
    const route = `${request.method} ${url.pathname}`;
    const answer = routes[route];

    if (answer === undefined) {
      return problem(404, `no route ${route}`);
    }

    const resolved = typeof answer === "function" ? answer(request) : answer;
    const { status, body } = isEnvelope(resolved) ? resolved : { status: 200, body: resolved };

    return new Response(body === undefined ? null : JSON.stringify(body), {
      status: status ?? 200,
      headers: { "content-type": "application/json" },
    });
  });

  vi.stubGlobal("fetch", fetch);

  return { calls, fetch };
}

function isEnvelope(value: unknown): value is { status?: number; body?: unknown } {
  return typeof value === "object" && value !== null && ("status" in value || "body" in value) && !("key" in value);
}

export function problem(status: number, detail: string): Response {
  return new Response(JSON.stringify({ type: "about:blank", title: "refused", status, detail }), {
    status,
    headers: { "content-type": "application/problem+json" },
  });
}

export function renderAt(path: string, element: ReactElement) {
  return render(
    <ThemeProvider storageKey="test.theme">
      <TooltipProvider>
        <MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>
      </TooltipProvider>
    </ThemeProvider>,
  );
}

export const aUser = {
  id: "0199a000-0000-7000-8000-000000000001",
  kind: "user" as const,
  name: "maintainer",
  administrator: true,
  email: "maintainer@example.test",
  owner: null,
  token: { prefix: "ha_abcd", created_at: "2026-09-02T10:00:00Z" },
  metadata: null,
  metadata_reported_at: null,
};

export const aProject = {
  key: "PLAN",
  name: "hostingaffe",
  instructions_page: null,
  created_at: "2026-09-02T10:00:00Z",
  updated_at: "2026-09-02T10:00:00Z",
};
