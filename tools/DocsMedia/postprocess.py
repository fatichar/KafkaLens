#!/usr/bin/env python3
"""Turn raw headless captures into website-ready images.

- Adds a simple window frame (title bar with the KafkaLens icon) around every main-window capture.
- Composites `<name>__dlg.png` dialogs centred over their dimmed `<name>__bg.png` main window,
  and also writes the framed dialog alone as `<name>-dialog`.
- Writes optimised PNGs (and .webp) to the output directory.

Usage: postprocess.py <raw-dir> <out-dir> <logo.png> [--dark]
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

raw, out, logo_path = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
dark = "--dark" in sys.argv
out.mkdir(parents=True, exist_ok=True)

SCALE = 1.5
BAR = int(34 * SCALE)
RADIUS = int(10 * SCALE)
FONT = "/usr/share/fonts/truetype/inter/Inter-Medium.ttf"


def font(size):
    for p in [FONT, "/usr/share/fonts/opentype/inter/Inter-Medium.otf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"]:
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default()


def frame(img: Image.Image, title: str, dark_bar: bool, dim_controls=False) -> Image.Image:
    w, h = img.size
    bar_bg = (43, 43, 46) if dark_bar else (236, 238, 242)
    fg = (220, 220, 220) if dark_bar else (40, 44, 52)
    win = Image.new("RGBA", (w, h + BAR), bar_bg + (255,))
    win.paste(img.convert("RGBA"), (0, BAR))
    d = ImageDraw.Draw(win)
    d.line([(0, BAR - 1), (w, BAR - 1)], fill=(60, 60, 64) if dark_bar else (210, 214, 220), width=1)
    icon = Image.open(logo_path).convert("RGBA").resize((int(BAR * 0.55),) * 2, Image.LANCZOS)
    win.alpha_composite(icon, (int(12 * SCALE), (BAR - icon.height) // 2))
    f = font(int(13 * SCALE))
    d.text((int(12 * SCALE) + icon.width + int(8 * SCALE), BAR / 2), title, fill=fg, font=f, anchor="lm")
    # window controls (generic, platform-neutral)
    cx = w - int(18 * SCALE)
    for kind in ("close", "max", "min"):
        y = BAR / 2
        s = 5 * SCALE
        if kind == "close":
            d.line([(cx - s, y - s), (cx + s, y + s)], fill=fg, width=2)
            d.line([(cx - s, y + s), (cx + s, y - s)], fill=fg, width=2)
        elif kind == "max":
            d.rectangle([cx - s, y - s, cx + s, y + s], outline=fg, width=2)
        else:
            d.line([(cx - s, y), (cx + s, y)], fill=fg, width=2)
        cx -= int(36 * SCALE)
    # rounded corners + border
    mask = Image.new("L", win.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h + BAR - 1], RADIUS, fill=255)
    win.putalpha(mask)
    ImageDraw.Draw(win).rounded_rectangle([0, 0, w - 1, h + BAR - 1], RADIUS,
                                          outline=(80, 80, 86) if dark_bar else (190, 195, 204), width=2)
    return win


def shadowed(win: Image.Image, pad=int(28 * SCALE)) -> Image.Image:
    w, h = win.size
    canvas = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([pad, pad + 6, pad + w, pad + h + 6], RADIUS, fill=(0, 0, 0, 90))
    sh = sh.filter(ImageFilter.GaussianBlur(pad / 2.2))
    canvas.alpha_composite(sh)
    canvas.alpha_composite(win, (pad, pad))
    return canvas


def save(img: Image.Image, name: str):
    img.save(out / f"{name}.png", optimize=True)
    img.save(out / f"{name}.webp", quality=88, method=6)
    print("wrote", name)


TITLES = {
    "edit-clusters": "Edit Clusters & Clients",
    "add-cluster": "Add/Edit Cluster",
    "preferences": "Preferences",
    "preferences-browser": "Preferences",
    "preferences-formatters": "Preferences",
}

for p in sorted(raw.glob("*.png")):
    stem = p.stem
    if stem.endswith("__dlg"):
        continue
    is_dark = dark or stem.startswith("dark")
    if stem.endswith("__bg"):
        name = stem[:-4]
        bg = frame(Image.open(p), "KafkaLens", is_dark).convert("RGBA")
        veil = Image.new("RGBA", bg.size, (0, 0, 0, 110))
        mask = bg.getchannel("A")
        bg = Image.composite(Image.alpha_composite(bg, veil), bg, mask)
        dlg = shadowed(frame(Image.open(raw / f"{name}__dlg.png"), TITLES.get(name, name), is_dark))
        save(dlg, f"{name}-dialog")
        x = (bg.width - dlg.width) // 2
        y = (bg.height - dlg.height) // 2 + BAR // 2
        bg.alpha_composite(dlg, (max(0, x), max(0, y)))
        save(shadowed(bg), name)
    else:
        save(shadowed(frame(Image.open(p), "KafkaLens", is_dark)), stem)
