// A stand-in OIDC provider for the end-to-end suite: just enough of one for the app's
// sign-in library and the API's bearer handler to talk to it as they would to a real
// provider, cross-origin, so the shell's Content-Security-Policy is exercised by a real
// sign-in (SEC-1, SEC-3). Dependency-free: node:http and node:crypto.
//
// It serves two tenants from one process:
//
// - `issuer` (http://localhost:<port>): every endpoint on the issuer's own origin, like
//   most providers.
// - `splitIssuer` (http://localhost:<port>/split): the token endpoint on a second origin
//   (http://localhost:<tokenPort>), like AWS Cognito's hosted domain, so a deployment needs
//   Auth:ExtraOrigins for it.
//
// What it does not do: hold a session, ask anyone anything, or check a client secret.
// /authorize answers at once with a code for one fixed member, and /end-session only
// records the call and sends the browser back.
import {
  createHash,
  createPrivateKey,
  createPublicKey,
  createSign,
  generateKeyPairSync,
  randomBytes,
} from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import http from "node:http";
import { tmpdir } from "node:os";
import path from "node:path";

/** Who the stub signs in, and the access token's audience the app is configured with. */
export const SUBJECT = "e2e-oidc-member";
export const AUDIENCE = "foo-calendar-e2e";
export const CLIENT_ID = "foo-calendar-e2e-spa";

/** The claim values appsettings.json maps each permission to by default, so the member
 * holds every permission. */
const PERMISSIONS = ["PeopleManage", "ReportsRead", "HolidaysSync", "AuditRead"];

const base64url = (data) => Buffer.from(data).toString("base64url");

/** The signing key, kept between runs on this machine. The app's bearer handler caches the
 * provider's keys and re-fetches them at most every few minutes, so a stub with a new key
 * each run would see its tokens refused by instances still running from the last one. */
function signingKey() {
  const file = path.join(tmpdir(), "foo-calendar-e2e-oidc-stub-key.pem");
  if (existsSync(file)) return createPrivateKey(readFileSync(file));
  const { privateKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  writeFileSync(file, privateKey.export({ format: "pem", type: "pkcs8" }), { mode: 0o600 });
  return privateKey;
}

/**
 * Starts the provider on `port`, with the split tenant's token endpoint on `tokenPort`.
 * Resolves to { issuer, splitIssuer, calls, accessToken, close }: `calls` records every
 * request it answered ({ origin, path, query, form }), and `accessToken()` mints one as
 * /token would, for the test to call the API with directly.
 */
export async function startOidcStub({ port = 9000, tokenPort = 9001 } = {}) {
  const issuer = `http://localhost:${port}`;
  const splitIssuer = `${issuer}/split`;
  const tokenOrigin = `http://localhost:${tokenPort}`;

  const privateKey = signingKey();
  const publicJwk = createPublicKey(privateKey).export({ format: "jwk" });
  const kid = createHash("sha256").update(publicJwk.n).digest("base64url").slice(0, 16);
  const jwks = { keys: [{ ...publicJwk, kid, alg: "RS256", use: "sig" }] };

  /** An RS256 JWT signed with the key the JWKS serves. */
  function sign(claims) {
    const now = Math.floor(Date.now() / 1000);
    const header = base64url(JSON.stringify({ alg: "RS256", typ: "JWT", kid }));
    const payload = base64url(JSON.stringify({ iat: now, nbf: now, exp: now + 3600, ...claims }));
    const signature = createSign("RSA-SHA256").update(`${header}.${payload}`).sign(privateKey);
    return `${header}.${payload}.${base64url(signature)}`;
  }

  const accessToken = (iss = issuer) =>
    sign({ iss, sub: SUBJECT, aud: AUDIENCE, permissions: PERMISSIONS, scope: "openid profile" });

  const tokens = (iss, nonce) => ({
    access_token: accessToken(iss),
    id_token: sign({
      iss,
      sub: SUBJECT,
      aud: CLIENT_ID,
      given_name: "E2E",
      family_name: "Provider Member",
      ...(nonce ? { nonce } : {}),
    }),
    refresh_token: randomBytes(16).toString("hex"),
    token_type: "Bearer",
    expires_in: 3600,
    scope: "openid profile email offline_access",
  });

  const discovery = (iss, tokenEndpoint) => ({
    issuer: iss,
    authorization_endpoint: `${iss}/authorize`,
    token_endpoint: tokenEndpoint,
    jwks_uri: `${iss}/jwks`,
    end_session_endpoint: `${iss}/end-session`,
    response_types_supported: ["code"],
    subject_types_supported: ["public"],
    id_token_signing_alg_values_supported: ["RS256"],
    code_challenge_methods_supported: ["S256"],
    grant_types_supported: ["authorization_code", "refresh_token"],
  });

  const root = { iss: issuer, path: "", tokenEndpoint: `${issuer}/token` };
  const split = { iss: splitIssuer, path: "/split", tokenEndpoint: `${tokenOrigin}/split/token` };

  /** The tenant and endpoint a request is for, or null. The split tenant's token endpoint
   * is on the second origin, and only there. */
  function route(origin, pathname) {
    if (origin === tokenOrigin) return pathname === "/split/token" ? [split, "/token"] : null;
    const tenant = pathname.startsWith("/split/") ? split : root;
    const endpoint = pathname.slice(tenant.path.length);
    return tenant === split && endpoint === "/token" ? null : [tenant, endpoint];
  }

  /** Codes handed out by /authorize and not yet redeemed. */
  const codes = new Map();
  const calls = [];

  function handle(origin, req, res, form) {
    const url = new URL(req.url, origin);
    const query = Object.fromEntries(url.searchParams);
    calls.push({ origin, path: url.pathname, query, form });

    // Everything the browser fetches is cross-origin to the app.
    res.setHeader("Access-Control-Allow-Origin", "*");
    res.setHeader("Access-Control-Allow-Headers", "*");
    res.setHeader("Cache-Control", "no-store");
    if (req.method === "OPTIONS") return res.writeHead(204).end();

    const json = (status, body) =>
      res.writeHead(status, { "Content-Type": "application/json" }).end(JSON.stringify(body));
    const redirect = (location) => res.writeHead(302, { Location: location }).end();

    const routed = route(origin, url.pathname);
    if (!routed) return json(404, { error: "not_found" });
    const [tenant, endpoint] = routed;

    if (req.method === "GET" && endpoint === "/.well-known/openid-configuration")
      return json(200, discovery(tenant.iss, tenant.tokenEndpoint));

    if (req.method === "GET" && endpoint === "/jwks") return json(200, jwks);

    if (req.method === "GET" && endpoint === "/authorize") {
      if (query.client_id !== CLIENT_ID || query.response_type !== "code" || !query.redirect_uri)
        return json(400, { error: "invalid_request" });
      const code = randomBytes(16).toString("hex");
      codes.set(code, { tenant, ...query });
      const back = new URL(query.redirect_uri);
      back.searchParams.set("code", code);
      if (query.state) back.searchParams.set("state", query.state);
      return redirect(back.href);
    }

    if (req.method === "POST" && endpoint === "/token") {
      if (form.grant_type === "refresh_token") return json(200, tokens(tenant.iss));
      const issued = codes.get(form.code);
      codes.delete(form.code);
      // PKCE as the library sends it: S256 of the verifier must be the challenge it sent.
      const challenge = createHash("sha256").update(form.code_verifier ?? "").digest("base64url");
      if (
        form.grant_type !== "authorization_code" ||
        !issued ||
        issued.tenant !== tenant ||
        issued.redirect_uri !== form.redirect_uri ||
        issued.code_challenge !== challenge
      )
        return json(400, { error: "invalid_grant" });
      return json(200, tokens(tenant.iss, issued.nonce));
    }

    if (req.method === "GET" && endpoint === "/end-session")
      return query.post_logout_redirect_uri
        ? redirect(query.post_logout_redirect_uri)
        : res.writeHead(200, { "Content-Type": "text/plain" }).end("Signed out.");

    return json(404, { error: "not_found" });
  }

  // On both loopback addresses: "localhost" is either, depending on who resolves it (the
  // browser, the API in its container on the host's network, this process), and nothing
  // off the machine needs to reach a provider that signs anyone in.
  const listen = (listenPort, host) => {
    const origin = `http://localhost:${listenPort}`;
    const server = http.createServer((req, res) => {
      let body = "";
      req.on("data", (chunk) => (body += chunk));
      req.on("end", () => handle(origin, req, res, Object.fromEntries(new URLSearchParams(body))));
    });
    return new Promise((resolve, reject) => {
      server.once("error", (error) =>
        // A machine without IPv6 has no ::1 to listen on, and nothing that would use it.
        host === "::1" && error.code === "EADDRNOTAVAIL" ? resolve(null) : reject(error),
      );
      server.listen(listenPort, host, () => resolve(server));
    });
  };

  const servers = (
    await Promise.all(
      [port, tokenPort].flatMap((p) => [listen(p, "127.0.0.1"), listen(p, "::1")]),
    )
  ).filter(Boolean);

  return {
    issuer,
    splitIssuer,
    tokenOrigin,
    calls,
    accessToken,
    close: () => Promise.all(servers.map((s) => new Promise((r) => s.close(r)))),
  };
}
