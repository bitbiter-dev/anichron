"""Writes the made-up E2E library into mira/ (README.md)."""

import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from pillow_heif import register_heif_opener

register_heif_opener()

library = Path(__file__).parent / "mira"
width, height = 1200, 900
make, model = "Anichron", "Test Camera"


def scene(seed: int, size: tuple[int, int]) -> Image.Image:
    rng = np.random.default_rng(seed)
    w, h = size
    y, x = np.mgrid[0:h, 0:w] / max(w, h)
    base = np.stack(
        [np.sin(x * rng.uniform(4, 9) + rng.uniform(0, 6)), np.cos(y * rng.uniform(4, 9)), np.sin((x + y) * 5)],
        axis=-1,
    )
    image = Image.fromarray(((base + 1) * 110).astype(np.uint8))
    draw = ImageDraw.Draw(image)
    for _ in range(140):
        cx, cy, r = rng.integers(0, w), rng.integers(0, h), rng.integers(12, 70)
        fill = tuple(int(c) for c in rng.integers(0, 256, 3))
        if rng.random() < 0.5:
            draw.rectangle([cx - r, cy - r // 2, cx + r, cy + r // 2], fill=fill, outline=(0, 0, 0), width=4)
        else:
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill, outline=(255, 255, 255), width=4)
    return image


def exif(taken: str) -> bytes:
    data = Image.Exif()
    data[0x010F] = make
    data[0x0110] = model
    data.get_ifd(0x8769)[0x9003] = taken
    return data.tobytes()


def still(name: str, seed: int, taken: str) -> None:
    scene(seed, (width, height)).save(library / name, quality=88, exif=exif(taken))


def burst(names: list[str], seed: int, taken: list[str]) -> None:
    canvas = scene(seed, (width + 60, height + 60))
    for frame, (name, at) in enumerate(zip(names, taken)):
        shift = frame * 8
        canvas.crop((shift, shift // 2, shift + width, shift // 2 + height)).save(
            library / name, quality=88, exif=exif(at)
        )


def ffmpeg(*args: str) -> None:
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", *args], check=True)


library.mkdir(exist_ok=True)
still("2025-06-13 garden.jpg", 1, "2025:06:13 12:00:00")
still("2025-06-15 kite.jpg", 2, "2025:06:15 12:00:00")
burst(
    [f"2025-06-15 waves {n}.jpg" for n in (1, 2, 3)],
    3,
    [f"2025:06:15 13:00:0{n}" for n in (0, 1, 2)],
)
scene(4, (width, height)).save(library / "2024-06-15 lantern.heic", quality=80, exif=exif("2024:06:15 12:00:00"))
ffmpeg(
    "-f", "lavfi", "-i", "testsrc2=size=640x480:rate=30:duration=2",
    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-an",
    "-metadata", "creation_time=2024-06-15T12:00:00Z",
    str(library / "2024-06-15 lantern.mov"),
)
ffmpeg(
    "-f", "lavfi", "-i", "testsrc2=size=640x480:rate=30:duration=3",
    "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest",
    "-metadata", "creation_time=2024-06-15T13:00:00Z",
    str(library / "2024-06-15 tone.mp4"),
)
