# Diagram library for hosting and installation maps

**Question:** Which library should draw navigable provider → machine and machine → installation diagrams in the React/TypeScript web application?

**Date:** 2026-09-20. Sources are the libraries' own documentation and repositories. This is a library choice for proposed views, not a product decision about what the maps mean.

## Recommendation

Use **React Flow (`@xyflow/react`) with Dagre (`@dagrejs/dagre`)** for the first version. The web application already uses React and TypeScript ([`package.json`](../../src/web/package.json)); React Flow supports typed custom React nodes, SVG edges, built-in pan/zoom and viewport controls, and keyboard navigation. Dagre supplies the directed tree layout that React Flow deliberately leaves to external libraries. Both are MIT licensed. [React Flow custom nodes](https://reactflow.dev/learn/customization/custom-nodes), [TypeScript](https://reactflow.dev/learn/advanced-use/typescript), [edges](https://reactflow.dev/learn/customization/custom-edges), [viewport](https://reactflow.dev/learn/concepts/the-viewport), [accessibility](https://reactflow.dev/learn/advanced-use/accessibility), [layout comparison](https://reactflow.dev/learn/layouting/layouting), [React Flow license](https://reactflow.dev/), [Dagre license](https://github.com/dagrejs/dagre/blob/master/LICENSE).

The current React Flow package declares `react` and `react-dom` peer ranges of `>=17`, which include this project's React 19. Its package metadata also declares MIT. That establishes package-level compatibility; an integration build and UI test would still be needed during implementation. [React Flow package metadata](https://github.com/xyflow/xyflow/blob/main/packages/react/package.json), [web dependencies](../../src/web/package.json).

Use custom provider, machine, and installation cards with ordinary links to detail pages. Put addresses inside machine cards, because an address is a field, not a separate domain entity ([Vision §5](../../Vision.md#5-non-goals-deliberate-boundaries)). Generate positions from recorded relationships and disable dragging, connecting, and deletion for these read-only views; React Flow exposes the corresponding interaction and keyboard props. The graph is a navigation surface over records, not an editor. [React Flow component API](https://reactflow.dev/api-reference/react-flow).

React Flow is **not a pure SVG diagram renderer**: its nodes are HTML `div` elements and its edges are SVG paths. It meets the requested graphical diagram view; if a standalone SVG file becomes a requirement, this choice needs a separate export path or reconsideration. [Nodes and edges](https://reactflow.dev/learn/concepts/terms-and-definitions), [custom edges](https://reactflow.dev/learn/customization/custom-edges).

## Alternatives

| Library | Fit | Why it is not the first choice |
| --- | --- | --- |
| [D3](https://d3js.org/) with [d3-hierarchy tree](https://d3js.org/d3-hierarchy/tree) and [d3-zoom](https://d3js.org/d3-zoom) | Excellent for a tailored SVG illustration; tree coordinates and pan/zoom are available; ISC license ([source](https://github.com/d3/d3/blob/main/LICENSE)). | D3 provides lower-level drawing and interaction primitives. We would need to build the node cards, focus and keyboard behavior, viewport controls, and React integration ourselves. That is useful if bespoke SVG rendering becomes the main goal. [D3 overview](https://github.com/d3/d3), [d3-zoom](https://d3js.org/d3-zoom). |
| [Cytoscape.js](https://js.cytoscape.org/) | Mature graph library with built-in and extensible layouts, pan/zoom, and MIT licensing; optimized for network visualization. | Its documented renderer is canvas-based, so ordinary linked React cards and SVG/DOM semantics are less direct. It is a better candidate if the product later needs large, interconnected network exploration rather than two small record hierarchies. This is an inference from its renderer and graph-oriented API. [Cytoscape.js documentation](https://js.cytoscape.org/). |

## Implementation notes and limits

- Start with Dagre because React Flow calls it a simple fit for trees. Dagre needs node dimensions; recompute layout when the visible graph changes. React Flow notes a Dagre limitation with nested subflows connected outside the group, so revisit [ELK](https://reactflow.dev/learn/layouting/layouting) if the maps gain compound groups and cross-group edges. [Layout guide](https://reactflow.dev/learn/layouting/layouting), [Dagre example](https://reactflow.dev/examples/layout/dagre).
- Keep a conventional list and detail links alongside the diagram. React Flow supplies keyboard-focusable nodes/edges and ARIA configuration, but accessibility of our custom cards and navigation still needs implementation and testing. [Accessibility guide](https://reactflow.dev/learn/advanced-use/accessibility).
- Collapse crowded branches and show installation counts before expanding them. React Flow recommends hiding parts of deep node trees for performance. Its own guide warns that unnecessary React updates hurt larger diagrams. [Performance guide](https://reactflow.dev/learn/advanced-use/performance).
- Server rendering is supported in React Flow 12 when node sizes and handle positions are supplied. Hostingaffe currently builds a client web application, so this is not a requirement for the first version. [SSR guide](https://reactflow.dev/learn/advanced-use/ssr-ssg-configuration), [`package.json`](../../src/web/package.json).
- No bundle-size or runtime benchmark was run. Measure those when representative maps and node counts exist; the documentation does not establish performance for this product's actual data.
