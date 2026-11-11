# Magick.NET decodes HEIC and HEIF stills

Status: accepted (#401, decided on #382)

## Decision

The Worker decodes HEIC and HEIF stills with Magick.NET (`Magick.NET-Q8-AnyCPU`, bundling libheif), in a decode step of its own. Everything else keeps decoding through ImageSharp, which has no HEIC decoder.

## Why

One package carries the same native build, and so the same libheif, for linux-x64, linux-arm64 and osx-arm64. It is Apache-2.0.

## Rejected

| Option | Why not |
|---|---|
| FFmpeg in the base image | Old versions can't open an iPhone photo, or return one tile of its grid |
| FFmpeg 9.x | Only as a third-party static build, without distro security updates |
| System libheif (LibHeifSharp or `heif-dec`) | Different libheif versions in the image and on Macs, as with Emgu CV in ADR 0009 |
| `Magick.NET-Q8-x64` + `-arm64` | Both define the same types, so referencing both fails the build |

Reconsider FFmpeg once the distro's FFmpeg merges tile grids.

## Consequences

- **Recognised by bytes:** an ISO-BMFF `ftyp` box with major brand `heic`, `heix` or `mif1` goes to `MagickHeifDecoder`. Everything else, AVIF included, goes to ImageSharp.
- **Upright exactly once:** libheif applies the container's `irot`/`imir`, which wins over EXIF. `AutoOrient` stays on the ImageSharp path.
- **Colour** is handled as for JPEG: the ICC profile is carried onto the proxies, not applied, and the HDR gain map is ignored.
- A large native library in the Worker image. The publish stage keeps only the target platform's copy, and no extra package is needed.
- libheif and libde265 are LGPL-3.0. Shipping the HEVC decoder was accepted by the maintainer.
- Only `MagickHeifDecoder` touches Magick.NET (`magick-stays-in-the-decoder`). Callers see `IUprightImageDecoder`.
