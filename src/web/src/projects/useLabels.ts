import { useCallback, useEffect, useState } from "react";
import { api, describe, type Schemas } from "@/api/client";

export type Label = Schemas["Label"];

/**
 * The label set of a project, asked once per load and shared by every place
 * that offers a choice from it — the issue form, the epic form and the list
 * filter. Each of them asking for itself meant the same list travelled again
 * on every mount, and a form opened from a list asked twice before it was
 * drawn once.
 *
 * A request that failed is not an answer worth keeping: it leaves the map, so
 * the next screen asks again rather than inheriting an empty set forever.
 */
const asked = new Map<string, Promise<Label[]>>();

function labelsOf(project: string): Promise<Label[]> {
  const pending = asked.get(project);

  if (pending !== undefined) {
    return pending;
  }

  const fresh = (async () => {
    try {
      const { data, error, response } = await api.GET("/projects/{key}/labels", {
        params: { path: { key: project } },
      });

      if (data === undefined) {
        throw new Error(describe(error, response.status));
      }

      return data;
    } catch (problem) {
      asked.delete(project);
      throw problem;
    }
  })();

  asked.set(project, fresh);

  return fresh;
}

/**
 * Ask again next time: the label set of this project was written to. Without a
 * project it forgets all of them, which is what a test that stood a new
 * instance in front of the client means.
 */
export function forgetLabels(project?: string): void {
  if (project === undefined) {
    asked.clear();
  } else {
    asked.delete(project);
  }
}

export type Labels = {
  /** What the project has, empty until the answer arrives or when it never did. */
  labels: Label[];
  /**
   * Create a label under this project and hand it back, so a picker can attach
   * what a writer just named. Throws the refusal as its message.
   */
  create: (name: string) => Promise<Label>;
};

export function useLabels(project: string | undefined): Labels {
  const [labels, setLabels] = useState<Label[]>([]);

  useEffect(() => {
    if (project === undefined) {
      return;
    }

    let current = true;

    void labelsOf(project).then(
      (known) => {
        if (current) setLabels(known);
      },
      () => {
        if (current) setLabels([]);
      },
    );

    return () => {
      current = false;
    };
  }, [project]);

  const create = useCallback(
    async (name: string): Promise<Label> => {
      const key = project!;
      const { data, error, response } = await api.POST("/projects/{key}/labels", {
        params: { path: { key } },
        body: { name, group: null, description: null },
      });

      if (data === undefined) {
        throw new Error(describe(error, response.status));
      }

      // The shared set grows with it, so the next screen sees the label
      // without a second round trip.
      const known = [...(asked.has(key) ? await labelsOf(key) : []), data];
      asked.set(key, Promise.resolve(known));
      setLabels(known);

      return data;
    },
    [project],
  );

  return { labels, create };
}
