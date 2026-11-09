# OpenCV feature matching decides whether two photos show the same scene

Status: accepted (#298, decided on #296)

## Decision

Burst detection compares the stored thumbnails of neighbouring frames with OpenCV feature matching (OpenCvSharp4):

- ORB keypoints, a ratio test and a RANSAC homography.
- The score is the share of keypoints that fit that one transform.
- Frames match at or above `Worker:BurstSimilarityThreshold`.

## Why

- **Not a perceptual hash.** A hash compares the whole picture, so a small pan moves every bit: on a real burst pair, dHash could not separate it from unrelated images. Feature matching scored the pair 0.52 against 0.07 for the closest unrelated pair.
- **OpenCvSharp4** is Apache-2.0, and ships native runtimes at one version for linux-x64, linux-arm64 and osx-arm64.

## Rejected

- **Emgu CV:** GPL or commercial, with a different OpenCV version per platform.
- **Our own matcher on ImageSharp:** we would own a detector, a descriptor and RANSAC under the 100% mutation gate.

## Consequences

- The Worker image carries a large native library. The publish stage deletes the other platforms' copies.
- The native library needs `libatomic1` on amd64, and `libtesseract5` and `libwebpdemux2` on arm64.
- Only `OrbThumbnailMatcher` touches OpenCV (`opencv-stays-in-the-matcher`). Callers see `IThumbnailMatcher`.
- Tests assert which side of the threshold a score falls on, never its exact value.
- A changed threshold applies only to photos checked from then on.
