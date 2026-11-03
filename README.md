# Deep & Sky

A dependency-free, public two-player browser game. Everything players see is in English. No sign-in or owner account is required.

- **Deep Dive:** obscure correct answers score more and move a submarine down.
- **Sky Rise:** familiar correct answers score more and lift a hot-air balloon up.
- Synchronized 3-second preview and 25-second answer window.
- Secret submissions, server-authoritative scoring, independent browser sessions, shared ready state, host-controlled finish, and rematches.
- Endless rounds. Authored categories plus validated English spelling variants are drawn without repeats until the selected deck is exhausted, then reshuffled. This is a finite answer bank, not unlimited unique AI generation.
- Familiarity tiers are editorial estimates, not survey percentages.

## Run

Requires Node.js 24 or newer; there are no external npm dependencies.

```sh
npm test
npm run build
npm start
```

The HTTP server listens on `0.0.0.0:$PORT` (default 10000). `/health` is a read-only health endpoint. SQLite stores rooms in `$DATA_DIR/game.sqlite` (default `.local/game.sqlite`). Old rooms expire after 24 hours of inactivity.

On Render's free compute plan, local storage is ephemeral: restarts, redeploys, or service sleep can clear rooms. Create a fresh room after a restart. A persistent DATA_DIR mount is optional if a paid persistent disk is added later; none is provisioned by this project.

No provider keys, paid model calls, ads, trackers, or client-side answer bank are used. No original Krillion assets or private APIs are included.
