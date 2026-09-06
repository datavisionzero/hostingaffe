import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { ReleasesView } from "./ReleasesView";

afterEach(() => vi.unstubAllGlobals());

const unreleased = {
  name: "unreleased",
  status: "open",
  description: "",
  published_at: null,
  published_by: null,
  issues: 2,
};

const published = {
  name: "0.3.0",
  status: "published",
  description: "The third cut.",
  published_at: "2026-08-30T09:00:00Z",
  published_by: { id: "0199a000-0000-7000-8000-000000000001", name: "maintainer" },
  issues: 7,
};

it("lists the open release first and links each one to its own screen", async () => {
  installInstance({ "GET /projects/PLAN/releases": [unreleased, published] });
  renderAt("/PLAN/releases", <Routes><Route path="/:project/releases" element={<ReleasesView />} /></Routes>);

  const items = await screen.findAllByRole("listitem");
  expect(items.map((item) => item.textContent)).toEqual([
    expect.stringContaining("unreleased"),
    expect.stringContaining("0.3.0"),
  ]);
  expect(screen.getByRole("link", { name: /unreleased/ })).toHaveAttribute("href", "/PLAN/releases/unreleased");
  expect(screen.getByRole("link", { name: /0\.3\.0/ })).toHaveAttribute("href", "/PLAN/releases/0.3.0");
  expect(screen.getByText("2 issues")).toBeInTheDocument();
  expect(screen.getByText(/maintainer/)).toBeInTheDocument();
});

it("says what it could not load rather than showing an empty list", async () => {
  installInstance({ "GET /projects/PLAN/releases": { status: 403, body: { detail: "No access to PLAN." } } });
  renderAt("/PLAN/releases", <Routes><Route path="/:project/releases" element={<ReleasesView />} /></Routes>);

  expect(await screen.findByText("No access to PLAN.")).toBeInTheDocument();
  expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
});
