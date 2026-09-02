import { describe, expect, it } from "vitest";

import { reconcileList, shallowEqual } from "./reconcile";

describe("shallowEqual", () => {
  it("matches flat objects field by field", () => {
    expect(shallowEqual({ a: 1, b: null }, { a: 1, b: null })).toBe(true);
    expect(shallowEqual({ a: 1 }, { a: 2 })).toBe(false);
    expect(shallowEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false);
    expect(shallowEqual({ a: 1, b: 2 }, { a: 1 })).toBe(false);
  });
});

describe("reconcileList", () => {
  const a = { id: 1, name: "Ada" };
  const b = { id: 2, name: "Grace" };

  it("returns the previous array itself when a refetch changed nothing", () => {
    const previous = [a, b];
    const fresh = [{ ...a }, { ...b }];
    expect(reconcileList(previous, fresh)).toBe(previous);
  });

  it("keeps unchanged elements' identity when one element changed", () => {
    const previous = [a, b];
    const fresh = [{ ...a }, { ...b, name: "Grace H." }];
    const merged = reconcileList(previous, fresh);
    expect(merged).not.toBe(previous);
    expect(merged[0]).toBe(a);
    expect(merged[1]).not.toBe(b);
    expect(merged[1]!.name).toBe("Grace H.");
  });

  it("keeps element identity across a reorder but returns a new array", () => {
    const previous = [a, b];
    const fresh = [{ ...b }, { ...a }];
    const merged = reconcileList(previous, fresh);
    expect(merged).not.toBe(previous);
    expect(merged[0]).toBe(b);
    expect(merged[1]).toBe(a);
  });

  it("handles additions and removals", () => {
    const c = { id: 3, name: "Alan" };
    expect(reconcileList([a, b], [{ ...a }, { ...b }, c])).toEqual([a, b, c]);
    const removed = reconcileList([a, b], [{ ...b }]);
    expect(removed).toHaveLength(1);
    expect(removed[0]).toBe(b);
  });

  it("treats a same-id element with changed fields as new", () => {
    const merged = reconcileList([a], [{ id: 1, name: "Ada L." }]);
    expect(merged[0]).not.toBe(a);
    expect(merged[0]!.name).toBe("Ada L.");
  });
});
