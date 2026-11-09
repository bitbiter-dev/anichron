# Proxies accept a media cookie

Status: accepted (decided on #347)

## Decision

`GET /api/v1/proxies/{assetId}/{type}` is the one route that also authenticates with a cookie, so `<img>` and `<video>` can load proxies. Every other route accepts only the bearer header.

- **The cookie:** `media_token` carries the access token and expires with it.
  - Attributes: `HttpOnly`, `Secure`, `Path=/api/v1/proxies`.
  - Set by login and by a cookie refresh; never by `/login/mobile` or a body refresh. Logout clears it.
  - `SameSite` follows the refresh cookie.
- **The scheme:** a second JWT scheme with the same validation, named only by the proxy route's policy. One class owns the scheme, the policy and the route, and keeps the scheme name private.
- **Must change password** is an authorization requirement, so the cookie cannot bypass it.

## Rejected

- **Signed URLs:** a credential in the URL lands in logs and shared links, cannot be revoked, and changes per listing, which defeats `immutable` caching.
- **Blob fetches with the header:** JavaScript per image, no native video streaming or range requests, and every image held in memory.

## Consequences

- No write route accepts the cookie, so CSRF cannot reach a write.
- Hard to reverse once screens use `<img src>`.
- After a sleep past the token's expiry, media returns `401` until the UI refreshes. The UI refreshes ahead of expiry and on becoming visible.
- A new route that wants the cookie is a decision to revisit here, not a policy to reuse.
