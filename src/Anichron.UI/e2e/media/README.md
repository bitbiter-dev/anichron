# E2E media

`mira/` is the made-up library the E2E Worker ingests. Every file is synthetic: drawn shapes, ffmpeg test patterns and a sine tone. None of them is a real photo.

`make-media.py` writes it. Run it from any directory with Pillow, pillow-heif and NumPy installed, and ffmpeg on `PATH`:

```bash
python3 src/Anichron.UI/e2e/media/make-media.py
```

The script decides each file's capture date, camera and kind. The spec's pinned day and expected memories (`../story.spec.ts`) follow from them, so change both together.

- The burst frames are one textured scene, shifted a few pixels per frame, a second apart, so the Worker's similarity check groups them.
- The Live Photo is a HEIC and a MOV with the same name, which is how the Worker pairs them.
