# DocsMedia — website screenshots & demo video

Renders the **real KafkaLens views** headlessly (Avalonia.Headless + Skia) against an in-memory
demo Kafka client, then turns the frames into the screenshots and the demo video used on
[kafkalens.com](https://kafkalens.com). No Kafka broker, display or app window is needed.

Not part of `KafkaLens.slnx`; nothing here ships with the app.

## Regenerate everything

```bash
tools/DocsMedia/generate.sh            # optional arg: work dir for intermediate frames
```

Writes `docs/assets/images/screenshots/*.webp` (+ `main.png` for social cards) and
`docs/assets/video/kafkalens-demo.mp4` (+ poster). Takes a few minutes; most of it is video frames.

Requirements: .NET 10 SDK, `python3` with Pillow, numpy and `edge-tts`, `ffmpeg`.
The voiceover uses Microsoft's online neural voices (only the narration text is sent); set
`KL_NO_AUDIO=1` to build a silent video offline, `KL_VOICE` to pick another voice. On Linux the `Inter` and
`JetBrains Mono` fonts are used; `fonts.conf` maps the Windows fonts the UI asks for
(`Consolas`, `Courier New`) to them.

## Pieces

| File | Role |
|---|---|
| `DemoApp.cs` | The real `App`, wired with temp settings, three demo clusters and `DemoKafkaClient`. |
| `DemoKafkaClient.cs` | Deterministic e-commerce demo data (orders, payments, shipments, binary metrics, logs) honouring position/count/partition. All names, emails and IDs are fictional. |
| `Studio.cs` | Headless driver: pump dispatcher, find controls, simulated mouse/keyboard, captures. |
| `ScreenshotScript.cs` | One capture per documented feature (`screens Light`), plus `dark-theme` (`screens Dark`). |
| `VideoScript.cs` | Scripted walkthrough; emits frames + `frames.tsv` (cursor, clicks, captions). |
| `postprocess.py` | Adds window chrome + shadow, composites dialogs over the dimmed main window. |
| `make_video.py` | Adds pointer, click ripples, captions, intro/outro cards; encodes H.264 MP4. Writes `timeline.json` (caption timings). |
| `narrate.py` | Voiceover (one spoken line per caption, `NARRATION` map) + synthesized music bed that ducks under the voice; muxes AAC into the MP4. |

To add a screenshot, add a step to `ScreenshotScript.cs` ending in `s.Capture("name")`
(or `s.CaptureDialog("name", window)`), re-run `generate.sh`, and reference
`assets/images/screenshots/name.webp` from the page.

When you change or add a `Say(...)` caption in `VideoScript.cs`, add its spoken line to
`NARRATION` in `narrate.py` (spelled for the ear, no symbols); otherwise the caption is read as-is.
