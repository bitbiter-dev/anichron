# Proxies accept a media cookie

Status: accepted (2026-10-29, #347). Decided by the maintainer on #347.

`GET /api/v1/proxies/{assetId}/{type}` is the one route that authenticates with a cookie as well
as the bearer header, so the web UI can load proxies through plain `<img>` and `<video>`, which
cannot send a header. Every other route accepts only the bearer header.

- **The cookie:** `media_token`, `HttpOnly`, `Secure`, `Path=/api/v1/proxies`, carrying the
  access token itself and expiring with it. `/login` and a refresh that renews the refresh
  cookie also set it; `/login/mobile` and a refresh with the token in the body never do;
  logout clears it with the same `Path`. Its `SameSite` follows the refresh cookie: `Strict` on
  the same origin, as ADR 0012 assumes, or `None` when `CORS__ALLOWEDORIGINS` is set.
- **The scheme:** a second JWT bearer scheme that reads the cookie, with the same validation as
  the default one. Only the proxy route's policy names it; the default scheme stays bearer-only.
  One class registers the scheme, owns that policy and maps the proxy route, and keeps the
  scheme name `private`, so no other route can name it.
- **Must change password** is an authorization requirement evaluated after either scheme, not a
  middleware that sees only the default scheme, so the cookie cannot bypass it.

## Why not signed URLs or blob fetches

Weighed in the technology comparison on #338 (Q11, Q15):

- **Signed URLs** put a credential in the URL, so it lands in logs, history and shared links,
  cannot be revoked before it expires, and changes on every listing, which defeats the
  `immutable` cache.
- **Fetching each proxy with the header into a blob URL** needs JavaScript per image, loses
  native video streaming and range requests, and holds every image in memory.

A cookie scoped to read-only routes keeps all of that native. CSRF cannot reach a write, because
no write route accepts the cookie, and a cross-site page that embeds a proxy can neither read it
nor guess an asset id.

## Consequences

- Hard to reverse once screens use `<img src>`: removing the cookie means every media element
  fetches through JavaScript.
- A phone that sleeps past the access token's expiry gets `401` on media until the UI refreshes;
  the UI refreshes ahead of expiry and when the page becomes visible again.
- A new route that needs the cookie is a decision to revisit here, not a policy to reuse.
