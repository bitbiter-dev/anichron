# Anichron

A self-hosted media flashback system. Crawls your a folder and surfaces your photos and videos as an Instagram Story-style "On This Day" experience.

## Status

[![CI](https://github.com/bitbiter-dev/anichron/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/bitbiter-dev/anichron/actions/workflows/ci.yml)
[![Coverage](https://raw.githubusercontent.com/bitbiter-dev/anichron/badges/coverage.svg)](https://bitbiter-dev.github.io/anichron/coverage/)
[![Mutation](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fbitbiter-dev%2Fanichron%2Fbadges%2Fmutation.json)](https://bitbiter-dev.github.io/anichron/mutation/mutation-report.html)

Early development — Follow progress in [Issues](https://github.com/bitbiter-dev/anichron/issues) and [Milestones](https://github.com/bitbiter-dev/anichron/milestones).

## Highlights

- Daily flashbacks grouped by year.
- Live Photo support and burst grouping
- Star, like, or hide memories to personalise future flashbacks
- Monthly, seasonal, and yearly recap views
- Daily notifications with a deep link to today's memories
- Multi-user — each person keeps their own interaction state; family libraries supported
- GPU-accelerated transcoding (Intel QuickSync → NVENC → AMF → software fallback)
- No cloud dependency — your NAS, your data — [AGPL-3.0](LICENSE)

## Get started

See the **[Self-Hosting guide](https://github.com/bitbiter-dev/anichron/wiki/Self-Hosting)** for prerequisites, quick start, and multi-user setup.

## Build the images

Run from the repository root; the build context is `src/`:

```bash
docker build -f src/Anichron.API/Dockerfile -t anichron-api:local src
docker build --secret id=sixlabors_license,src=sixlabors.lic -f src/Anichron.Worker/Dockerfile -t anichron-worker:local src
```

The Worker uses ImageSharp, which needs a [Six Labors licence key](https://sixlabors.com/pricing/) at build time. Put the key, and nothing else, in `sixlabors.lic` at the repository root (it is gitignored); the Worker build takes it as the build secret `sixlabors_license` and fails with "No Six Labors license found" without it. To pass the key from an environment variable instead, use `--secret id=sixlabors_license,env=SIXLABORS_LICENSE_KEY`.

## Documentation

The **[wiki](https://github.com/bitbiter-dev/anichron/wiki)** covers features, architecture, deployment decisions, and the release process.

## Contributing

Anichron is a solo project: bug reports are welcome, pull requests are not accepted. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[AGPL-3.0](LICENSE)