#!/usr/bin/env python3
"""Lays the frames Scripts/record-demo.ps1 filmed onto the README's recording, as Claudy's.

    python Scripts/make-demo-video.py film

Reads <film>/frames/<scene>/<ms>.png and writes docs/video-readme.gif (English captions) and
docs/video-readme.fr.gif (French captions); the app itself stays in English in both, as in
Claudy's. Claudy's recording sits on a red, blue and green wallpaper; Claudio's on a Windows-like
bloom of navy, blue, violet and cyan. The mascot of the title, the explosion and the end is drawn
from Claudy's exported frames, as Scripts/make-mascot-gifs.py draws it.
"""
import bisect
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / "claudy" / "Design"
FONT = ROOT / "src" / "Claudio.App" / "Assets" / "Fonts" / "Nunito.ttf"
DOCS = ROOT / "docs"

WIDTH, HEIGHT = 800, 578
FPS = 15
INSET = 28          # the transparent margin round Claudio's windows, where their shadow lives
KICKER = (125, 211, 252)  # the captions' accent: Windows' light blue, where Claudy's is coral

# Captions: (kicker, title, subtitle) per scene, English then French.
CAPTIONS = {
    "en": {
        "title": ("", "Claudio", "Your real Claude quotas, on your Windows desktop."),
        "strip": ("On your desktop", "Your quota,\nin a strip.", "Always on top. Drag it anywhere."),
        "card": ("Usage", "Real numbers,\nor none.", "The same figures as claude.ai › Usage\nand /usage."),
        "details": ("Usage", "Split by model\nand by project.", "Seven days of tokens, read from Claude\nCode's local transcripts."),
        "sessions": ("Sessions", "Every session\nopen on your PC.", "The ones waiting for your answer come first."),
        "ports": ("Ports", "What Claude Code\nleft listening.", "Orphaned ports and MCP servers, gone in\na click."),
        "island": ("", "Or at the top of the screen.", "Hover it and the island opens."),
        "room": ("Agent room", "Every agent at its desk.", "One room per project. Each screen shows what its agent is doing."),
        "moves": ("Agent room", "It moves when its work changes.", "Code, tests, build, research: read from its last tool call."),
        "desk": ("Agent room", "Click a desk.", "What its agent was asked, its last calls, the files it wrote."),
        "overload": ("", "At 100 %, Claudio\ntakes it badly.", "The laptop goes first."),
        "end": ("", "Claudio", "scoop install claudio"),
    },
    "fr": {
        "title": ("", "Claudio", "Tes vrais quotas Claude, sur ton bureau Windows."),
        "strip": ("Sur ton bureau", "Ton quota,\nen une bande.", "Toujours au-dessus. Déplace-la où tu veux."),
        "card": ("Usage", "Des chiffres réels,\nou aucun.", "Les mêmes chiffres que claude.ai › Usage\net /usage."),
        "details": ("Usage", "Par modèle\net par projet.", "Sept jours de tokens, lus dans les transcripts\nlocaux de Claude Code."),
        "sessions": ("Sessions", "Chaque session\nouverte sur ton PC.", "Celles qui attendent ta réponse d'abord."),
        "ports": ("Ports", "Ce que Claude Code\na laissé en écoute.", "Ports orphelins et serveurs MCP, partis\nen un clic."),
        "island": ("", "Ou en haut de l'écran.", "Survole-le et l'îlot s'ouvre."),
        "room": ("Salle des agents", "Chaque agent à son bureau.", "Une salle par projet. Chaque écran montre ce que fait son agent."),
        "moves": ("Salle des agents", "Il bouge quand son travail change.", "Code, tests, build, recherche : lu dans son dernier appel d'outil."),
        "desk": ("Salle des agents", "Clique un bureau.", "Ce qu'on a demandé à l'agent, ses derniers appels, ses fichiers écrits."),
        "overload": ("", "À 100 %, Claudio\nle vit mal.", "L'ordinateur part en premier."),
        "end": ("", "Claudio", "scoop install claudio"),
    },
}

# Scene order and length in seconds; the filmed ones last as long as Claudio played them.
SCENES = [("title", 3.5), ("strip", 4), ("card", 5), ("details", 4.5), ("sessions", 6), ("ports", 4.5),
          ("island", 7), ("room", 6.5), ("moves", 7), ("desk", 5.5), ("overload", 7), ("end", 4.5)]
FADE = 0.35


def font(size, weight="Bold"):
    face = ImageFont.truetype(str(FONT), size)
    face.set_variation_by_name(weight)
    return face


def mono(size):
    for name in ("CascadiaMono.ttf", "consola.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return font(size, "SemiBold")


# The wallpaper: a bloom of light on navy, drawn once.

def wallpaper():
    base = Image.new("RGB", (WIDTH, HEIGHT), (8, 12, 34))
    glow = Image.new("RGB", (WIDTH, HEIGHT), (0, 0, 0))
    draw = ImageDraw.Draw(glow)
    for (x, y, rx, ry, colour) in [
        (130, 470, 420, 260, (36, 84, 255)),
        (620, 120, 380, 240, (124, 77, 255)),
        (720, 520, 300, 200, (20, 184, 220)),
        (300, 40, 260, 160, (212, 70, 170)),
        (430, 330, 300, 120, (60, 120, 255)),
    ]:
        draw.ellipse((x - rx, y - ry, x + rx, y + ry), fill=colour)
    glow = glow.filter(ImageFilter.GaussianBlur(110))
    image = ImageChops.add(base, glow)
    # Soft folds across it, as Windows' bloom has.
    folds = Image.new("L", (WIDTH, HEIGHT), 0)
    fd = ImageDraw.Draw(folds)
    for k in range(5):
        points = [(x, HEIGHT * (0.25 + 0.14 * k) + 40 * math.sin(x / 140 + k)) for x in range(-20, WIDTH + 40, 20)]
        fd.line(points, fill=40, width=34)
    folds = folds.filter(ImageFilter.GaussianBlur(26))
    light = Image.new("RGB", (WIDTH, HEIGHT), (190, 205, 255))
    return Image.composite(light, image, folds.point(lambda v: v // 2))


# The mascot, from Claudy's exported frames.

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
    alpha, below_alpha = top[3] / 255, below[3] / 255
    out = alpha + below_alpha * (1 - alpha)
    if out == 0:
        return (0, 0, 0, 0)
    mix = [round((t * alpha + b * below_alpha * (1 - alpha)) / out) for t, b in zip(top[:3], below[:3])]
    return tuple(mix) + (round(out * 255),)


def sprite(rows, cell, tint):
    image = Image.new("RGBA", (len(rows[0]) * cell, len(rows) * cell), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    cache = {}
    for y, line in enumerate(rows):
        for x, ink in enumerate(line):
            if ink not in cache:
                colour = (0, 0, 0, 0)
                for layer in INKS.get(ink, []):
                    colour = over(tint if layer == "tint" else rgba(token(layer)), colour)
                cache[ink] = colour
            colour = cache[ink]
            if colour[3]:
                draw.rectangle((x * cell, y * cell, (x + 1) * cell - 1, (y + 1) * cell - 1), fill=colour)
    return image


TYPING = load("mascot/typing.json")
OVERLOAD = load("mascot/overload.json")
CORAL = rgba(token("color.accent.coral"))
AMBER = rgba(token("color.accent.amber"))
DANGER = rgba(token("color.danger"))


def typing(t, cell, tint=CORAL):
    sequence = TYPING["sequence"]
    step = int(t * 1000 / TYPING["frameMs"]) % len(sequence)
    return sprite(TYPING["poses"][sequence[step]], cell, tint)


def explosion(t, cell):
    frames, durations, dead = OVERLOAD["frames"], OVERLOAD["frameMs"], OVERLOAD["deadLoopCount"]
    intro = len(frames) - dead
    ms = t * 1000
    for index in range(intro):
        if ms < durations[index]:
            return sprite(frames[index], cell, DANGER)
        ms -= durations[index]
    loop = sum(durations[intro:])
    ms %= loop
    for index in range(intro, len(frames)):
        if ms < durations[index]:
            return sprite(frames[index], cell, DANGER)
        ms -= durations[index]
    return sprite(frames[-1], cell, DANGER)


# Claudio's windows, as filmed.

class Clip:
    """One scene's frames, by the milliseconds since it began."""

    def __init__(self, folder):
        self.paths = sorted(folder.glob("*.png"), key=lambda path: int(path.stem)) if folder.exists() else []
        self.times = [int(path.stem) for path in self.paths]
        self.cache = {}

    def at(self, t):
        if not self.paths:
            return None
        index = max(0, bisect.bisect_right(self.times, t * 1000) - 1)
        if index not in self.cache:
            self.cache = {index: Image.open(self.paths[index]).convert("RGB")}
        return self.cache[index]


def card(frame, corner):
    """A floating window's card: its shadow margin cut off, its corners rounded."""
    inner = frame.crop((INSET, INSET, frame.width - INSET, frame.height - INSET))
    mask = Image.new("L", inner.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, inner.width - 1, inner.height - 1), radius=corner, fill=255)
    inner = inner.convert("RGBA")
    inner.putalpha(mask)
    return inner


def island(frame):
    """The island, cut out of the black its window holds round it."""
    image = frame.convert("RGBA")
    alpha = frame.convert("L").point(lambda v: 0 if v <= 1 else 255)
    image.putalpha(alpha)
    box = alpha.getbbox()
    return image.crop(box) if box else image


def scaled(image, scale):
    return image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)


def shadow(canvas, image, x, y, radius=16, offset=8, opacity=110):
    alpha = image.split()[3].point(lambda v: v * opacity // 255)
    pad = radius * 3
    layer = Image.new("L", (image.width + 2 * pad, image.height + 2 * pad), 0)
    layer.paste(alpha, (pad, pad))
    layer = layer.filter(ImageFilter.GaussianBlur(radius))
    canvas.paste((0, 0, 0), (x - pad, y - pad + offset), layer)


def place(canvas, image, x, y, with_shadow=True):
    if with_shadow:
        shadow(canvas, image, x, y)
    canvas.paste(image, (x, y), image)


# Captions.

def text_block(canvas, x, y, caption, size=30, width=None, centre=False):
    kicker, title, subtitle = caption
    draw = ImageDraw.Draw(canvas)
    if kicker:
        draw.ellipse((x, y + 5, x + 6, y + 11), fill=KICKER)
        draw.text((x + 12, y), kicker, font=font(12, "SemiBold"), fill=KICKER)
        y += 24
    heading = font(size, "Bold")
    for line in title.split("\n"):
        lx = x if not centre else (WIDTH - draw.textlength(line, font=heading)) / 2
        draw.text((lx, y), line, font=heading, fill=(255, 255, 255))
        y += int(size * 1.18)
    y += 8
    small = font(13, "Medium")
    for line in subtitle.split("\n"):
        lx = x if not centre else (WIDTH - draw.textlength(line, font=small)) / 2
        draw.text((lx, y), line, font=small, fill=(225, 230, 245))
        y += 18
    return y


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# Scenes.

def title_scene(canvas, t, words, end=False):
    mascot = typing(t, 6)
    place(canvas, mascot, (WIDTH - mascot.width) // 2, 110 if not end else 90, with_shadow=False)
    draw = ImageDraw.Draw(canvas)
    name = font(46, "Bold")
    y = 300 if not end else 280
    draw.text(((WIDTH - draw.textlength("Claudio", font=name)) / 2, y), "Claudio", font=name, fill=(255, 255, 255))
    if not end:
        small = font(15, "Medium")
        draw.text(((WIDTH - draw.textlength(words[2], font=small)) / 2, y + 66), words[2], font=small, fill=(225, 230, 245))
        return
    command = words[2]
    code = mono(15)
    w = draw.textlength(command, font=code)
    box = ((WIDTH - w) / 2 - 16, y + 70, (WIDTH + w) / 2 + 16, y + 102)
    draw.rounded_rectangle(box, radius=10, fill=(10, 14, 36), outline=(90, 110, 170))
    draw.text(((WIDTH - w) / 2, y + 77), command, font=code, fill=(240, 244, 255))
    link = "github.com/Endikk/Claudio"
    small = font(13, "Medium")
    draw.text(((WIDTH - draw.textlength(link, font=small)) / 2, y + 118), link, font=small, fill=(200, 210, 240))


def side_scene(canvas, t, clip, caption, length):
    """The caption on the left, the card on the right; a card taller than the frame scrolls."""
    frame = clip.at(t)
    text_block(canvas, 34, 190, caption)
    if frame is None:
        return
    corner = 22 if frame.height - 2 * INSET < 120 else 28
    image = card(frame, corner)
    scale = 1.08 if image.height < 120 else 0.92
    image = scaled(image, scale)
    x = WIDTH - image.width - 40
    if image.height <= HEIGHT - 40:
        y = (HEIGHT - image.height) // 2
    else:
        travel = image.height - (HEIGHT - 40)
        y = 20 - round(travel * ease((t - 0.8) / max(length - 1.8, 0.1)))
    place(canvas, image, x, y)


def island_scene(canvas, t, clip, caption):
    frame = clip.at(t)
    if frame is not None:
        image = scaled(island(frame), 1.25)
        place(canvas, image, (WIDTH - image.width) // 2, 0)
    text_block(canvas, 0, 430, caption, size=34, centre=True)


def room_scene(canvas, t, clip, caption):
    text_block(canvas, 28, 14, caption, size=22)
    frame = clip.at(t)
    if frame is None:
        return
    image = scaled(card(frame, 28), 0.68)
    place(canvas, image, (WIDTH - image.width) // 2, HEIGHT - image.height - 8)


def overload_scene(canvas, t, clip, caption):
    text_block(canvas, 34, 210, caption)
    blast = 2.2
    mascot = typing(t, 6, AMBER) if t < blast else explosion(t - blast, 6)
    place(canvas, mascot, WIDTH - 60 - mascot.width + (40 if t >= blast else 0), 70 if t < blast else 40, with_shadow=False)
    frame = clip.at(t)
    if frame is not None:
        image = scaled(card(frame, 22), 1.2)
        place(canvas, image, WIDTH - image.width - 40, HEIGHT - image.height - 60)


def render(language, film):
    background = wallpaper()
    clips = {name: Clip(film / "frames" / name) for name, _ in SCENES}
    words = CAPTIONS[language]
    frames = []
    scenes = []

    def draw(name, t, length):
        canvas = background.copy()
        if name in ("title", "end"):
            title_scene(canvas, t, words[name], end=name == "end")
        elif name == "island":
            island_scene(canvas, t, clips[name], words[name])
        elif name in ("room", "moves", "desk"):
            room_scene(canvas, t, clips[name], words[name])
        elif name == "overload":
            overload_scene(canvas, t, clips[name], words[name])
        else:
            side_scene(canvas, t, clips[name], words[name], length)
        return canvas

    for index, (name, length) in enumerate(SCENES):
        count = round(length * FPS)
        for step in range(count):
            t = step / FPS
            image = draw(name, t, length)
            # Each scene fades in from the one before.
            if index > 0 and t < FADE:
                previous, before = SCENES[index - 1]
                image = Image.blend(draw(previous, before - 1 / FPS, before), image, ease(t / FADE))
            elif index == 0 and t < FADE:
                image = Image.blend(background, image, ease(t / FADE))
            frames.append(image)
            scenes.append(index)
    return frames, scenes


def mascots():
    """The mascot's inks, large, so every scene's palette holds them however small it is shown."""
    sheet = Image.new("RGB", (WIDTH, HEIGHT), (8, 12, 34))
    images = [typing(0, 9), typing(0, 9, AMBER), explosion(0.3, 6), explosion(3, 6)]
    x = 0
    for image in images:
        sheet.paste(image, (x, 0), image)
        x += image.width
    return sheet


MASCOTS = mascots()


def save(frames, scenes, path):
    # One palette per scene, from a few of its frames: a scene keeps its colours from frame to
    # frame, so what does not move is not written again, and the room and the wallpaper each get
    # the colours they need.
    palettes = {}
    for scene in sorted(set(scenes)):
        members = [frame for frame, index in zip(frames, scenes) if index == scene]
        picks = [members[0], members[len(members) // 2], members[-1], MASCOTS]
        sample = Image.new("RGB", (WIDTH, HEIGHT * len(picks)))
        for k, frame in enumerate(picks):
            sample.paste(frame, (0, HEIGHT * k))
        palettes[scene] = sample.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    images = [frame.quantize(palette=palettes[scene], dither=Image.Dither.NONE) for frame, scene in zip(frames, scenes)]
    images[0].save(path, save_all=True, append_images=images[1:], duration=round(1000 / FPS), loop=0, optimize=False)
    print(f"{path.relative_to(ROOT)}: {len(images)} frames, {path.stat().st_size / 1e6:.1f} MB")


def main():
    film = Path(sys.argv[1] if len(sys.argv) > 1 else "film").resolve()
    for language, name in (("en", "video-readme.gif"), ("fr", "video-readme.fr.gif")):
        frames, scenes = render(language, film)
        save(frames, scenes, DOCS / name)


if __name__ == "__main__":
    main()
