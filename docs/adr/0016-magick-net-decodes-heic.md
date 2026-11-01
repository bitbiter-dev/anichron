# Magick.NET decodes HEIC and HEIF stills

Status: accepted (2026-11-01, #401). Decided by the maintainer on #382 (2026-10-31).

iPhones store photos as HEIC, and ImageSharp has no HEIC/HEIF decoder, so every iPhone photo failed
to ingest with `UnknownImageFormatException`. The Worker now decodes HEIC and HEIF stills with
Magick.NET (`Magick.NET-Q8-AnyCPU` 15.0.0, which bundles libheif 1.23.6 and libde265 1.1.3) in a
decode step of its own. JPEG, PNG and TIFF keep decoding through ImageSharp.

## Why not FFmpeg or the system libheif

Measured on #382 with the maintainer's iPhone 15 Pro Max photo (a 45-tile grid, `irot` 270):

| Option | Why (not) |
| --- | --- |
| **Magick.NET** | Apache-2.0. One package carries the same native build, and so the same libheif, for linux-x64, linux-arm64 and osx-arm64 (developers' Macs). |
| FFmpeg, noble's 6.1.1 | Can't open the file ("moov atom not found"). |
| FFmpeg, Ubuntu 26.04's 8.0.1 | Returns one 640×896 tile of the 45-tile grid. |
| FFmpeg 9.x | Merges the grid, but only as a third-party static build, without the distro's security updates. |
| LibHeifSharp on the system libheif, or `heif-dec` per photo | The image would get apt's libheif 1.17.6 while the Macs get Homebrew's 1.23.7: the version mismatch that ruled out Emgu CV in ADR 0009. CI would need an apt step, and `heif-dec` adds a process and a PNG round trip per photo. |

Reconsider FFmpeg when the distro FFmpeg in the Worker's base image merges tile grids.

## Why the AnyCPU package

#382 named the `Magick.NET-Q8-x64` and `Magick.NET-Q8-arm64` packages. Each ships its own managed
assembly (`Magick.NET-Q8-x64.dll`, `Magick.NET-Q8-arm64.dll`) defining the same `ImageMagick` types,
so referencing both fails the build (CS0433), and referencing one leaves the other architecture
without a decoder. `Magick.NET-Q8-AnyCPU` has one managed assembly and the same native libraries,
byte for byte (#401).

## Consequences

- **Recognised by bytes, not by extension.** `UprightImageDecoder` sends a file to the HEIF decoder
  when it starts with an ISO-BMFF `ftyp` box whose major brand is `heic`, `heix` or `mif1`. Every
  other file, AVIF with major brand `avif` (#396) included, goes to ImageSharp; only the major brand
  is read. The HEIF decoder pins the format
  rather than letting ImageMagick detect it.
- **Upright exactly once.** libheif applies the container's `irot`/`imir`, and the pixels are copied
  into an `Image<Rgb24>` without metadata, so nothing is left for `AutoOrient` to turn again.
  `AutoOrient` stays on the ImageSharp path (#220). The container wins over EXIF, as the HEIF spec
  says: with its EXIF orientation patched to 1 or 3, the fixture still matches its stored pixels
  (rendered by macOS `sips`) turned by `irot`.
- **Colour** is handled as on the JPEG path: 8-bit RGB values as stored, and the ICC profile (Display
  P3 on an iPhone) carried onto the proxies, not applied. The HDR gain map is ignored (#398).
- **A native library in the Worker image:** it grows by 41 MB on amd64 and 37 MB on arm64. The
  publish output holds every platform's copy, so the Dockerfile's publish stage deletes all but the
  target's (`win-*`, `linux-musl-x64`, `osx-arm64` and the other Linux architecture). The library
  links only against what the noble runtime image already has, so no package is installed for it.
  Checked before this was merged: the fixture decodes to 3024×4032 inside the built image on
  linux/amd64 and linux/arm64, and on macOS.
- **Licences:** Magick.NET is Apache-2.0, ImageMagick has its own Apache-2.0-style licence, and
  libheif and libde265 are LGPL-3.0. All are compatible with AGPL-3.0. libde265 is an HEVC decoder,
  and shipping it in the image was accepted by the maintainer on #382.
- Only `MagickHeifDecoder` touches Magick.NET (architecture rule `magick-stays-in-the-decoder`).
  Callers see `IUprightImageDecoder`: original bytes in, an upright `Image<Rgb24>` out.
- One library for every format is #399 (post-1.0).
