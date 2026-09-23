import type { ComponentType } from "react";
import { screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { renderAt } from "@/shared/testing";
import { InstallationDiagram } from "./InstallationDiagram";
import { installationDiagram } from "./installationMapData";
import type { DiagramItem } from "./diagramLayout";

vi.mock("@xyflow/react", () => ({ Handle: () => null, Position: { Left: "left", Right: "right" } }));
vi.mock("./Diagram", () => ({
  Diagram: ({ items, nodeTypes }: { items: DiagramItem[]; nodeTypes: Record<string, ComponentType<{ data: Record<string, unknown> }>> }) =>
    <div>{items.map((item) => {
      const Card = nodeTypes[item.type];
      return <Card key={item.id} data={item.data} />;
    })}</div>,
}));

it("links installation nodes and prints every distinct domain and missing deployment", () => {
  const graph = installationDiagram({
    machine: "host", name: "Example host", status: "active", avatar: null, avatar_color: null,
    installations: [
      { key: "site", name: "Example site", software: "site", role: "application", status: "active",
        urls: ["https://one.example.test/a", "https://two.example.test", "https://one.example.test/b"],
        latest_deployment_at: null },
    ],
  }, false);
  renderAt("/", <InstallationDiagram {...graph} />);
  expect(screen.getByRole("link", { name: "Example host" })).toHaveAttribute("href", "/machines/host");
  expect(screen.getByRole("link", { name: "Example site" })).toHaveAttribute("href", "/installations/site");
  expect(screen.getByText("one.example.test")).toBeInTheDocument();
  expect(screen.getByText("two.example.test")).toBeInTheDocument();
  expect(screen.getByText("No deployment recorded")).toBeInTheDocument();
});
