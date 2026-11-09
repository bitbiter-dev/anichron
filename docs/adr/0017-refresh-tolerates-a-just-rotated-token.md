# Refresh tolerates a just-rotated token

Status: accepted (decided on #345)

## Decision

`POST /api/v1/auth/refresh` rotates the refresh token, and treats a replayed token as theft: it revokes every session the user has. One exception: presenting the token rotated last, within **30 seconds** of its rotation, revokes its successor and issues a fresh pair instead.

- The window is a constant, not a setting.
- The successor is revoked because issued tokens are stored hashed and cannot be handed out again.
- Two simultaneous refreshes with one token cannot both rotate it. The second counts as reuse, which the window covers.
- The web UI serialises refreshes across tabs with a Web Lock. The window covers what the lock cannot: a response that never arrived.

## Rejected

- **Only the Web Lock:** a phone that loses a refresh response would revoke all of the user's sessions on its next refresh.
- **Only the window:** tabs would routinely replay the same cookie and revoke each other's tokens.
- **No reuse detection:** a stolen token would never be noticed.

## Consequences

- A stolen token replayed within 30 s of the owner's refresh is accepted once. The owner's next refresh then trips reuse detection and ends both sessions.
- The window is part of the refresh contract; removing it signs out phones that lose a response.
