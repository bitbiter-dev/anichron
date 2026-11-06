# Refresh tolerates a just-rotated token

Status: accepted (2026-11-06, #345). Decided by the maintainer on #345 (2026-11-06).

`POST /api/v1/auth/refresh` rotates the refresh token and treats a replayed one as theft: it
revokes every session the user has. That stays, with one exception. A refresh that presents the
token rotated last, within **30 seconds** of its rotation, revokes the token that replaced it and
issues a fresh pair instead of revoking every session. Reuse after the 30 seconds, or of any older
token, still revokes every session.

- **The window** is a constant, not a setting.
- **The successor is revoked,** because the issued tokens are stored hashed and cannot be handed
  out again. A browser keeps one refresh cookie, so the last `Set-Cookie` wins and nothing still
  holds the revoked token; a mobile client keeps only the pair it received last.
- **Two refreshes with the same token at the same moment** cannot both rotate it: the rotation is
  guarded so the second one is treated as reuse, which the window then covers.
- **The web UI serialises its refreshes** across tabs with a Web Lock (`navigator.locks`), so a
  waiting tab refreshes with the cookie the first tab already rotated. The window covers what the
  lock cannot: a refresh whose response, and with it the `Set-Cookie`, never arrived.

## Why not one of them alone

- **Only the Web Lock** keeps the server strict, but a phone that sleeps or loses its connection
  while a refresh is in flight is ordinary for a mobile-first UI. The server rotated the token, the
  browser never saw the new cookie, and its next refresh replays the old one: every session of
  the user is revoked for losing a response.
- **Only the window** leaves tabs racing. Every tab refreshes ahead of expiry and when it becomes
  visible again, so the same cookie would be replayed routinely, each replay revoking a sibling's
  fresh token and the window doing work it was meant to do rarely.
- **No reuse detection** gives up noticing a stolen refresh token at all.

## Consequences

- A refresh token stolen and replayed within 30 seconds of the owner's own refresh is accepted
  once; the owner's next refresh presents the revoked successor, which trips reuse detection and
  ends both sessions. Outside that window, theft detection is unchanged.
- The window is part of the refresh contract every client may rely on; removing it signs out
  phones that lose a refresh response.
