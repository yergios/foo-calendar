import { apiFetch } from "@/api";

/**
 * Claiming a name on a deployment with no identity provider. Only these routes exist in
 * that mode; on an OIDC deployment the API doesn't map them at all.
 *
 * Goes through apiFetch like every other endpoint, so a rejection carries the API's own
 * message ("(anonymous)" is reserved, the name is too long) instead of a bare status line.
 * The Authorization header it attaches is ignored: these routes are anonymous, which they
 * have to be, being what runs before anyone has an identity.
 */
export async function chooseIdentity(username: string): Promise<void> {
  await apiFetch(`/api/identity`, {
    method: "POST",
    body: JSON.stringify({ username }),
  });
}

export async function forgetIdentity(): Promise<void> {
  await apiFetch(`/api/identity`, { method: "DELETE" });
}
