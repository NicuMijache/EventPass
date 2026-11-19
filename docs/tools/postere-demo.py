# Generează posterele evenimentelor de demonstrație (docs/demo/postere/*.jpg).
# Sunt imagini abstracte, desenate din cod (fără fotografii sau imagini preluate de altundeva).
# Utilizare: python docs/tools/postere-demo.py   (are nevoie de Pillow: pip install pillow)
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = Path(__file__).resolve().parent.parent / "demo" / "postere"
W, H = 1200, 1600
FONT_DIR = Path("C:/Windows/Fonts")


def font(name, size):
    for candidate in (FONT_DIR / name, Path(name)):
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default(size)


# (fișier, titlu, oraș și dată, locație, culoarea de sus, culoarea de jos, motiv)
POSTERS = [
    ("jazz.jpg", "Nocturne Jazz\nSession", "CLUJ-NAPOCA   12.02.2027", "Form Space", (28, 24, 48), (196, 72, 28), "lines"),
    ("standup.jpg", "Seara de\nstand-up", "IAȘI   26.01.2027", "Club Subsol", (246, 196, 40), (232, 92, 20), "circles"),
    ("simfonii.jpg", "Simfonii de\nprimăvară", "IAȘI   04.04.2027", "Filarmonica Moldova", (210, 208, 204), (92, 90, 88), "circles"),
    ("devfest.jpg", "DevFest\nStudențesc", "TIMIȘOARA   10.03.2027", "Centrul Multifuncțional", (12, 40, 64), (20, 150, 140), "grid"),
    ("retro.jpg", "Retro Party\n90s", "BUCUREȘTI   30.01.2027", "Club Control", (232, 40, 120), (60, 20, 140), "stripes"),
    ("teatru.jpg", "Visul unei\nnopți de vară", "BUCUREȘTI   20.03.2027", "Teatrul Odeon", (14, 40, 30), (120, 150, 80), "arcs"),
    ("atelier.jpg", "Atelier de\nfotografie", "CLUJ-NAPOCA   06.02.2027", "Casa Tranzit", (240, 236, 228), (180, 160, 130), "frame"),
]


def gradient(top, bottom):
    img = Image.new("RGB", (W, H))
    px = img.load()
    for y in range(H):
        for x in range(W):
            t = min(1.0, max(0.0, (y * 0.8 + x * 0.35) / (H * 0.8 + W * 0.35)))
            px[x, y] = tuple(int(a + (b - a) * t) for a, b in zip(top, bottom))
    return img


def motif(img, kind, rnd, light):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ink = (255, 255, 255) if not light else (30, 30, 30)
    if kind == "circles":
        for _ in range(3):
            r = rnd.randint(180, 360)
            cx, cy = rnd.randint(150, W - 100), rnd.randint(100, H - 600)
            # cercuri albe, translucide, și pe fundalurile deschise (cele închise ar fi prea grele)
            d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 255, 255, 46 if not light else 110))
    elif kind == "lines":
        for i in range(6):
            y0 = 380 + i * 34
            d.line((0, y0 + 300, W, y0 - 120), fill=ink + (90,), width=3)
        d.ellipse((560, 520, 920, 880), fill=ink + (40,))
        d.ellipse((120, 760, 620, 1260), fill=ink + (30,))
    elif kind == "grid":
        for x in range(60, W, 90):
            for y in range(60, H - 560, 90):
                r = 6 if rnd.random() > 0.15 else 16
                d.ellipse((x - r, y - r, x + r, y + r), fill=ink + (120 if r > 6 else 60,))
        d.rounded_rectangle((640, 240, 1080, 680), radius=60, outline=ink + (160,), width=6)
    elif kind == "stripes":
        for i in range(-10, 30):
            x0 = i * 70
            d.polygon([(x0, 0), (x0 + 30, 0), (x0 + 30 - 700, H), (x0 - 700, H)], fill=ink + (22,))
        d.ellipse((330, 260, 870, 800), outline=ink + (200,), width=10)
        d.ellipse((470, 400, 730, 660), fill=ink + (60,))
    elif kind == "arcs":
        for i in range(9):
            r = 140 + i * 60
            d.arc((W - r - 80, 120 - r // 3, W - 80 + r, 120 + r * 2 - r // 3), 90, 200, fill=ink + (110,), width=4)
        d.ellipse((860, 140, 1000, 280), fill=(250, 240, 200, 210))
    elif kind == "frame":
        d.rectangle((140, 200, 1060, 940), outline=ink + (190,), width=8)
        d.rectangle((220, 280, 980, 860), fill=ink + (18,))
        d.ellipse((470, 440, 730, 700), outline=ink + (200,), width=14)
        d.ellipse((560, 530, 640, 610), fill=ink + (200,))
    layer = layer.filter(ImageFilter.GaussianBlur(0.6))
    img.paste(layer, (0, 0), layer)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for i, (file, title, meta, venue, top, bottom, kind) in enumerate(POSTERS):
        rnd = random.Random(i * 7 + 3)
        img = gradient(top, bottom)
        light = sum(top) > 600
        motif(img, kind, rnd, light)
        d = ImageDraw.Draw(img)
        ink = (255, 255, 255) if not light else (24, 24, 24)
        d.text((80, 80), meta, font=font("segoeui.ttf", 40), fill=ink)
        lines = title.split("\n")
        size = 118 if max(len(l) for l in lines) <= 14 else 100
        y = H - 200 - len(lines) * (size + 14)
        for line in lines:
            d.text((80, y), line, font=font("segoeuib.ttf", size), fill=ink)
            y += size + 14
        d.text((80, H - 130), venue, font=font("segoeui.ttf", 44), fill=ink)
        img.save(OUT / file, "JPEG", quality=86, optimize=True)
        print("scris:", OUT / file)


if __name__ == "__main__":
    main()
