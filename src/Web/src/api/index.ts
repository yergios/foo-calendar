import { resolveAccessToken } from "@/auth";
import { usePersonStore } from "@/stores/person";

// Matches CurrentPersonAccessor's exact message on the backend. An admin can delete
// someone else's profile out from under them, so if that person's own browser still
// has it cached, this is the signal to forget it, so they see "set up your profile"
// again instead of quietly failing every request as a ghost.
const PERSON_NOT_FOUND_MESSAGE = "No person exists for this account yet.";

async function getAuthHeaders(): Promise<HeadersInit> {
  const token = await resolveAccessToken();
  return {
    "Content-Type": "application/json",
    Authorization: `Bearer ${token}`,
  };
}

export async function apiFetchRaw(input: string, init?: RequestInit): Promise<Response> {
  const headers = await getAuthHeaders();
  return fetch(input, { ...init, headers: { ...headers, ...init?.headers } });
}

/**
 * Builds the error to throw for a non-OK response. ASP.NET's default exception handler
 * returns a ProblemDetails body with a human-readable `detail` field, for example
 * "This absence overlaps one you already have." Use it when present, and fall back
 * to the plain status code otherwise.
 */
export async function errorForResponse(res: Response): Promise<Error> {
  const detail = await res
    .clone()
    .json()
    .then((body: unknown) => (body as { detail?: string })?.detail)
    .catch(() => undefined);

  return new Error(detail ?? `API error: ${res.status} ${res.statusText}`);
}

export async function apiFetch(input: string, init?: RequestInit): Promise<Response> {
  const res = await apiFetchRaw(input, init);

  if (!res.ok) {
    const error = await errorForResponse(res);
    if (error.message === PERSON_NOT_FOUND_MESSAGE) usePersonStore().clearPerson();
    throw error;
  }

  return res;
}

/** Convenience wrapper for the common "fetch, then parse JSON" case. */
export async function apiJson<T>(input: string, init?: RequestInit): Promise<T> {
  const res = await apiFetch(input, init);
  return res.json();
}
