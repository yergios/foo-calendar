import { beforeEach, describe, expect, it, vi } from "vitest";

import { writeCache } from "./browserCache";
import { createLoadOnce } from "./loadOnce";
import { stubLocalStorage } from "./localStorageStub";

describe("createLoadOnce", () => {
  beforeEach(stubLocalStorage);

  it("[UI-7] single-flights concurrent callers and serves later calls from memory", async () => {
    const fetcher = vi.fn().mockResolvedValue("value");
    const { loadOnce } = createLoadOnce(fetcher);
    const [first, second] = await Promise.all([loadOnce(), loadOnce()]);
    expect(first).toBe("value");
    expect(second).toBe("value");
    await loadOnce();
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("[UI-7] retries after a failure instead of caching it", async () => {
    const fetcher = vi.fn().mockRejectedValueOnce(new Error("down")).mockResolvedValue("up");
    const { loadOnce } = createLoadOnce(fetcher);
    await expect(loadOnce()).rejects.toThrow("down");
    await expect(loadOnce()).resolves.toBe("up");
  });

  it("[UI-7] hydrates from the cache for instant paint but still revalidates", async () => {
    writeCache("swr.v1", "stale");
    const fetcher = vi.fn().mockResolvedValue("fresh");
    const { data, loadOnce } = createLoadOnce<string>(fetcher, "swr.v1");
    // The cached value is on screen before any fetch...
    expect(data.value).toBe("stale");
    // ...but does not count as loaded: the first call is the revalidate.
    await loadOnce();
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(data.value).toBe("fresh");
  });

  it("[UI-7] keeps the previous value's identity when reconcile says nothing changed", async () => {
    const previous = { list: [1, 2] };
    writeCache("swr.v1", previous);
    const fetcher = vi.fn().mockResolvedValue({ list: [1, 2] });
    const reconcile = vi.fn((prev: { list: number[] }) => prev);
    const { data, loadOnce } = createLoadOnce(fetcher, "swr.v1", reconcile);
    const hydrated = data.value;
    await loadOnce();
    expect(reconcile).toHaveBeenCalledTimes(1);
    expect(data.value).toBe(hydrated);
  });

  it("[UI-7] invalidate() forces the next call to refetch and reconcile again", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce({ n: 1 }).mockResolvedValueOnce({ n: 2 });
    const { data, loadOnce, invalidate } = createLoadOnce(fetcher);
    await loadOnce();
    expect(data.value).toEqual({ n: 1 });
    invalidate();
    await loadOnce();
    expect(data.value).toEqual({ n: 2 });
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("[UI-7] writes the merged value back to the cache on success", async () => {
    const fetcher = vi.fn().mockResolvedValue("fresh");
    const { loadOnce } = createLoadOnce<string>(fetcher, "swr.v1");
    await loadOnce();
    const { data } = createLoadOnce<string>(() => Promise.resolve("unused"), "swr.v1");
    expect(data.value).toBe("fresh");
  });
});
