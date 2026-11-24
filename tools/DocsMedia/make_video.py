#!/usr/bin/env python3
"""Compose the KafkaLens demo video from frames rendered by `DocsMedia <out> video`.

Adds an animated pointer, click ripples, captions, an intro and an outro card, and encodes
H.264 MP4 with ffmpeg.

Usage: make_video.py <frames-dir> <logo.png> <out.mp4>
"""
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

frames_dir, logo_path, out_path = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
FPS = 25
SCALE = 1.2  # must match VideoScript.Scale

FONT_DIRS = ["/usr/share/fonts/opentype/inter", "/usr/share/fonts/truetype/inter"]


def font(weight, size):
    for d in FONT_DIRS:
        for ext in ("otf", "ttf"):
            p = Path(d) / f"Inter-{weight}.{ext}"
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", size)


rows = [line.rstrip("\n").split("\t") for line in (frames_dir / "frames.tsv").open(encoding="utf-8-sig")]
rows = [r + [""] * (5 - len(r)) for r in rows if r and r[0]]
W, H = Image.open(frames_dir / rows[0][0]).size

logo = Image.open(logo_path).convert("RGBA")
F_CAPTION = font("Medium", 34)
F_TITLE = font("Bold", 96)
F_SUB = font("Medium", 40)
F_SMALL = font("Regular", 30)


# ── pointer sprite ───────────────────────────────────────────────────────
def make_cursor(size=34):
    s = 4  # supersample
    img = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    pts = [(0, 0), (0, 26), (7, 20), (12, 31), (17, 29), (12, 18.5), (21, 18.5)]
    pts = [(x * s * size / 34 + 2 * s, y * s * size / 34 + 2 * s) for x, y in pts]
    d = ImageDraw.Draw(img)
    d.polygon(pts, fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=int(1.6 * s))
    img = img.resize((size, size), Image.LANCZOS)
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 110), (2, 3), img.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(2))
    shadow.alpha_composite(img)
    return shadow


CURSOR = make_cursor()


def gradient(w, h, top=(18, 24, 48), bottom=(40, 32, 88)):
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / (h - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return g.resize((w, h))


def card(lines):
    img = gradient(W, H).convert("RGBA")
    d = ImageDraw.Draw(img)
    lg = logo.resize((220, 220), Image.LANCZOS)
    y = H // 2 - 300
    img.alpha_composite(lg, ((W - lg.width) // 2, y))
    y += lg.height + 50
    for text, f, color in lines:
        d.text((W // 2, y), text, font=f, fill=color, anchor="mt")
        y += f.size + 34
    return img.convert("RGB")


INTRO = card([
    ("KafkaLens", F_TITLE, (255, 255, 255)),
    ("A fast desktop browser for Apache Kafka", F_SUB, (210, 218, 255)),
    ("Windows  ·  macOS  ·  Linux", F_SMALL, (160, 170, 210)),
])
OUTRO = card([
    ("Try KafkaLens", F_TITLE, (255, 255, 255)),
    ("Free download at kafkalens.com", F_SUB, (210, 218, 255)),
    ("Open source  ·  github.com/fatichar/KafkaLens", F_SMALL, (160, 170, 210)),
])


def caption_layer(text, alpha):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    if not text or alpha <= 0:
        return layer
    d = ImageDraw.Draw(layer)
    l, t, r, b = d.textbbox((0, 0), text, font=F_CAPTION)
    pw, ph = r - l + 64, b - t + 36
    x0, y0 = (W - pw) // 2, H - ph - 70
    d.rounded_rectangle([x0, y0, x0 + pw, y0 + ph], 18, fill=(17, 24, 39, int(215 * alpha)))
    d.text((W // 2, y0 + ph / 2), text, font=F_CAPTION, fill=(255, 255, 255, int(255 * alpha)), anchor="mm")
    return layer


def ripple(frame_img, x, y, age):
    # expanding ring for ~10 frames after a click
    if age is None or age > 10:
        return
    r = 10 + age * 3.2
    a = int(200 * (1 - age / 10))
    layer = Image.new("RGBA", frame_img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse([x - r, y - r, x + r, y + r], outline=(37, 99, 235, a), width=4)
    frame_img.alpha_composite(layer)


ff = subprocess.Popen([
    "ffmpeg", "-y", "-loglevel", "error",
    "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
    "-c:v", "libx264", "-preset", "slow", "-crf", "24", "-pix_fmt", "yuv420p",
    "-tune", "animation", "-movflags", "+faststart", str(out_path),
], stdin=subprocess.PIPE)


def emit(img):
    ff.stdin.write(img.convert("RGB").tobytes())


def fade(a, b, n):
    for i in range(n):
        emit(Image.blend(a, b, (i + 1) / n))


# intro
black = Image.new("RGB", (W, H), (0, 0, 0))
fade(black, INTRO, 12)
for _ in range(int(3.0 * FPS)):
    emit(INTRO)

prev_caption, caption_age, last_click = None, 0, None
composed_first = None
composed_last = None
for i, (name, cx, cy, click, cap) in enumerate(rows):
    img = Image.open(frames_dir / name).convert("RGBA")
    x, y = float(cx) * SCALE, float(cy) * SCALE
    if click == "1":
        last_click = i
    ripple(img, x, y, None if last_click is None else i - last_click)
    img.alpha_composite(CURSOR, (int(x) - 2, int(y) - 2))
    if cap != prev_caption:
        prev_caption, caption_age = cap, 0
    caption_age += 1
    img.alpha_composite(caption_layer(cap, min(1.0, caption_age / 6)))
    rgb = img.convert("RGB")
    if composed_first is None:
        composed_first = rgb
        fade(INTRO, rgb, 15)
    emit(rgb)
    composed_last = rgb

fade(composed_last, OUTRO, 15)
for _ in range(int(4.0 * FPS)):
    emit(OUTRO)
fade(OUTRO, black, 12)

ff.stdin.close()
ff.wait()
print("wrote", out_path)
