#!/usr/bin/env python3
"""Draws the README's mascot GIFs from the frames Claudy exports, as Claudio draws them.

    python Scripts/make-mascot-gifs.py

Reads claudy/Design/ (palette, typing, overload, wave and the colour tokens) and writes
docs/claudio-typing.gif, docs/claudio-overload.gif and docs/claudio-wave.gif. Every cell is a
square of CELL pixels, painted with the inks the app uses, on a transparent background.
"""
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / "claudy" / "Design"
DOCS = ROOT / "docs"
CELL = 4


def load(name):
    return json.loads((DESIGN / name).read_text(encoding="utf-8"))


TOKENS = load("claudy.tokens.json")
INKS = load("mascot/palette.json")["inks"]


def token(path):
    node = TOKENS
    for part in path.split("."):
        node = node[part]
    return node["$value"]


def rgba(hex_value):
    digits = hex_value.lstrip("#")
    alpha = int(digits[6:8], 16) if len(digits) == 8 else 255
    return tuple(int(digits[i:i + 2], 16) for i in (0, 2, 4)) + (alpha,)


def over(top, below):
    """One colour painted over another, as the layers of an ink stack."""
    alpha, below_alpha = top[3] / 255, below[3] / 255
    out = alpha + below_alpha * (1 - alpha)
    if out == 0:
        return (0, 0, 0, 0)
    mix = [round((t * alpha + b * below_alpha * (1 - alpha)) / out) for t, b in zip(top[:3], below[:3])]
    return tuple(mix) + (round(out * 255),)


TINT = rgba(token("color.accent.coral"))


def paint(ink):
    layers = INKS.get(ink)
    if not layers:
        return None
    colour = (0, 0, 0, 0)
    for layer in layers:
        colour = over(TINT if layer == "tint" else rgba(token(layer)), colour)
    return colour


def render(rows, width=None, height=None, left=0, top=0):
    width = width or len(rows[0])
    height = height or len(rows)
    image = Image.new("RGBA", (width * CELL, height * CELL), (0, 0, 0, 0))
    pixels = image.load()
    for y, line in enumerate(rows):
        for x, ink in enumerate(line):
            colour = paint(ink)
            # A GIF pixel is opaque or clear: the faint ground shadow under the wave is left out.
            if colour is None or colour[3] < 128:
                continue
            for dy in range(CELL):
                for dx in range(CELL):
                    pixels[(x + left) * CELL + dx, (y + top) * CELL + dy] = colour
    return image


def indexed(frame):
    """A palette image whose index 255 is reserved for the clear pixels."""
    opaque = Image.new("RGB", frame.size, (0, 0, 0))
    opaque.paste(frame, mask=frame.split()[3])
    image = opaque.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    palette = image.getpalette()[:255 * 3] + [0, 0, 0]
    image.putpalette(palette)
    clear = frame.split()[3].point(lambda alpha: 255 if alpha == 0 else 0)
    image.paste(255, mask=clear)
    return image


def save(name, frames, durations):
    images = [indexed(frame) for frame in frames]
    images[0].save(DOCS / name, save_all=True, append_images=images[1:], duration=durations,
                   loop=0, disposal=2, transparency=255, optimize=False)
    print(f"docs/{name}: {len(frames)} frames")


def typing():
    data = load("mascot/typing.json")
    poses = data["poses"]
    frames = [render(poses[step]) for step in data["sequence"]]
    save("claudio-typing.gif", frames, [data["frameMs"]] * len(frames))


def overload():
    """The explosion once, then the dead loop a few times, on the larger grid it needs."""
    data = load("mascot/overload.json")
    frames = data["frames"]
    durations = data["frameMs"]
    dead = data["deadLoopCount"]
    intro = list(range(len(frames) - dead))
    loop = list(range(len(frames) - dead, len(frames)))
    order = intro + loop * 6
    save("claudio-overload.gif", [render(frames[i]) for i in order], [durations[i] for i in order])


def wave():
    data = load("mascot/wave.json")
    frames = data["frames"]
    used = [y for frame in frames for y, row in enumerate(frame["rows"]) if set(row) != {"."}]
    first, last = min(used), max(used)
    images = [render(frames[step["frame"]]["rows"][first:last + 1]) for step in data["sequence"]]
    save("claudio-wave.gif", images, [step["ms"] for step in data["sequence"]])


if __name__ == "__main__":
    typing()
    overload()
    wave()
