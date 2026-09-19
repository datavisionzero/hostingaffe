import { describe, expect, it } from "vitest";
import { values } from "./changes";

function change(over: Partial<{ field: string; old_value: string | null; new_value: string | null }> = {}) {
  return { field: "os", old_value: null, new_value: null, ...over };
}

describe("what a change of the history reads as", () => {
  it("leaves a birth that would print the subject back with nothing to print", () => {
    const one = change({ field: "created", new_value: "compose.override.yml" });

    expect(values(one, "compose.override.yml")).toEqual({ old: null, new: null });
  });

  it("keeps a birth whose value is its own, not the subject's address", () => {
    const one = change({ field: "created", new_value: "1.4.0" });

    expect(values(one, "logaffe-prod").new).toBe("1.4.0");
  });

  // The rule is a birth's, and only a birth's: a field that happens to be set
  // to the subject's own key says something, and it is not a repetition.
  it("keeps another field that happens to carry the subject's address", () => {
    const one = change({ field: "machine", new_value: "ex44" });

    expect(values(one, "ex44").new).toBe("ex44");
  });

  it("writes a moment the way every other date on the screen is written", () => {
    const one = change({ field: "measured_at", new_value: "2026-09-19T08:00:00.000000Z" });

    expect(values(one).new).toBe(new Date("2026-09-19T08:00:00.000000Z").toLocaleString());
    expect(values(one).new).not.toBe("2026-09-19T08:00:00.000000Z");
  });

  it.each([
    "Debian 13",
    "2026-09-19",
    "2026-13-45T08:00:00Z",
    "1.4.0",
    "v2026-09-19T08:00:00Z",
  ])("leaves %s alone, because nothing is guessed", (value) => {
    expect(values(change({ new_value: value })).new).toBe(value);
  });

  it("has nothing to print where the row has nothing", () => {
    expect(values(change({ old_value: "", new_value: null }))).toEqual({ old: null, new: null });
  });

  it("writes both sides where both are moments", () => {
    const one = change({
      field: "measured_at",
      old_value: "2026-09-01T08:00:00Z",
      new_value: "2026-09-19T08:00:00Z",
    });

    expect(values(one)).toEqual({
      old: new Date("2026-09-01T08:00:00Z").toLocaleString(),
      new: new Date("2026-09-19T08:00:00Z").toLocaleString(),
    });
  });
});
