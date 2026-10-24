# OpenCV feature matching decides whether two photos show the same scene

Status: accepted (2026-10-24, #298). Decided by the maintainer on #296 (option 4A).

Burst detection joins a photo to a neighbouring frame of the same camera when their stored 300 px
thumbnails show the same scene. That judgement is OpenCV feature matching through OpenCvSharp4:
ORB keypoints (500 per thumbnail), a 0.75 ratio test, and a RANSAC homography with a 5 px
threshold. The score is the share of keypoints that fit that one transform, and two frames match
when it reaches `Worker:BurstSimilarityThreshold` (0.25).

## Why not a perceptual hash

Measured on #296, on 300 px thumbnails of the maintainer's IMG_4386/4387 (3 s apart, panned about
5%, dense foliage) against 8 unrelated images:

| Method | Sample pair | Closest unrelated pair |
| --- | --- | --- |
| dHash, bits differing of 64 | 22 | 18 (no separation) |
| wHash | 10 | 18 |
| ORB + RANSAC, matching share | 0.52 (260 inliers) | 0.07 (the same wall from about 40° apart) |

A hash compares the whole picture, so a small pan moves every bit; feature matching finds the same
points wherever they moved, and the RANSAC step rejects matches no single transform explains.

## Why OpenCvSharp4

| Option | Why (not) |
| --- | --- |
| **OpenCvSharp4** | Apache-2.0, compatible with AGPL-3.0. Native runtimes from one maintainer at one version for every platform we run: `OpenCvSharp4.official.runtime.linux-x64`, `OpenCvSharp4.runtime.linux-arm64`, `OpenCvSharp4.runtime.osx.arm64` (developers' machines). |
| Emgu CV | GPL-3.0 or commercial, and its runtimes are at different OpenCV versions per platform, so the image and developer machines would disagree. |
| Our own matcher on ImageSharp | No native dependency, but we would own a keypoint detector, descriptor and RANSAC, each constant and branch under the 100% mutation gate. |

## Consequences

- A native library of about 40 MB per architecture in the Worker image.
- `libOpenCvSharpExtern.so` for linux-arm64 links against `libtesseract.so.5` and
  `libwebpdemux.so.2`, which noble ships but the runtime image does not install; the Worker
  Dockerfile installs `libtesseract5` and `libwebpdemux2`. AMD64_DEPENDENCIES
  Scored inside the built image on both architectures before this was merged: the sample pair
  0.492, an unrelated image 0.000, the same as on macOS.
- Only `OrbThumbnailMatcher` touches OpenCV (architecture rule `opencv-stays-in-the-matcher`).
  Callers see `IThumbnailMatcher`: two thumbnails in, a score from 0 to 1 out.
- The tuning constants are named in that one class and pinned by tests: the committed sample pair
  (a 300 px copy without metadata) scores at or above the default threshold, generated unrelated
  images below it, and the guards and the share are tested on constructed keypoints. Tests assert
  which side of the threshold a real score falls, never its exact value, since floating point may
  differ between platforms.
- Bursts written under one threshold stay until a "recalculate all" request (out of scope), so a
  changed setting applies to photos checked from then on.
