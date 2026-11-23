#!/usr/bin/env python3
"""Draws the JellySearch plugin image (16:9, shown in Dashboard > Plugins and on the tiles).

    python3 assets/make-image.py            -> assets/jellysearch.png

A wall of posters, a magnifier lighting one of them, and filter chips around it (IMDb rating, decade,
genre, drive), in the style of the JellyUSB image. Drawn at 2x and downscaled. Needs Pillow.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1280, 720
S = 2  # supersampling
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "jellysearch.png")

FONT_TITLE = "/usr/share/fonts/truetype/ubuntu/Ubuntu-B.ttf"
FONT_TEXT = "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf"

PURPLE = (170, 92, 195)
BLUE = (0, 164, 220)


def s(v):
    return int(round(v * S))


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def gradient(size, top, bottom, horizontal=False):
    w, h = size
    base = Image.linear_gradient("L").resize((256, 256))
    if horizontal:
        base = base.rotate(90, expand=True)
    mask = base.resize((w, h))
    a = Image.new("RGBA", (w, h), top + (255,))
    b = Image.new("RGBA", (w, h), bottom + (255,))
    return Image.composite(b, a, mask)


def glow(canvas, center, radius, color, alpha):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = center
    d.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=color + (alpha,))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(radius * 0.45)))


def rounded_mask(size, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius=radius, fill=255)
    return m


POSTERS = [
    ("#3b2a6b", "#120c2c", "moon"), ("#0d5c7d", "#06233a", "wave"), ("#7a2e3b", "#2a0c16", "sun"),
    ("#2f6b3b", "#0c2a14", "hills"), ("#6b4a12", "#2a1a06", "star"), ("#24427a", "#0a1636", "city"),
    ("#5a2a6b", "#1e0c2a", "wave"), ("#7a5a1e", "#2e200a", "moon"), ("#1e5a5a", "#0a2424", "hills"),
    ("#6b2a4a", "#2a0c1c", "city"), ("#2a3a6b", "#0c122a", "sun"), ("#4a6b2a", "#1a2a0c", "star"),
]


def poster(top, bottom, motif, lit=False):
    pw, ph = s(150), s(222)
    img = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    body = gradient((pw, ph), hexrgb(top), hexrgb(bottom))
    img.alpha_composite(Image.composite(body, Image.new("RGBA", (pw, ph), (0, 0, 0, 0)), rounded_mask((pw, ph), s(12))))
    layer = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    white = (255, 255, 255, 120 if not lit else 200)
    cx = pw // 2
    if motif == "moon":
        d.ellipse((cx - s(30), s(46), cx + s(30), s(106)), fill=white)
        d.ellipse((cx - s(12), s(40), cx + s(42), s(96)), fill=hexrgb(top) + (255,))
    elif motif == "sun":
        d.ellipse((cx - s(32), s(50), cx + s(32), s(114)), fill=(255, 200, 120, 150 if not lit else 230))
        d.rectangle((0, s(112), pw, s(118)), fill=white)
    elif motif == "wave":
        for k in range(3):
            y = s(70 + k * 22)
            d.arc((s(10), y - s(20), pw - s(10), y + s(20)), 200, 340, fill=white, width=s(5))
    elif motif == "hills":
        d.polygon([(0, s(140)), (s(50), s(70)), (s(95), s(130)), (s(120), s(95)), (pw, s(140))], fill=white)
    elif motif == "star":
        d.regular_polygon((cx, s(82), s(34)), 5, rotation=0, fill=white)
    else:  # city
        for k, hgt in enumerate((60, 90, 45, 75, 55)):
            x0 = s(14 + k * 25)
            d.rectangle((x0, s(140 - hgt), x0 + s(20), s(140)), fill=white)
    # title lines at the bottom
    d.rounded_rectangle((s(18), s(170), pw - s(18), s(180)), radius=s(5), fill=(255, 255, 255, 150 if lit else 80))
    d.rounded_rectangle((s(18), s(190), pw - s(48), s(198)), radius=s(4), fill=(255, 255, 255, 110 if lit else 55))
    img.alpha_composite(layer)
    if not lit:
        dim = Image.new("RGBA", (pw, ph), (8, 10, 30, 120))
        img.alpha_composite(Image.composite(dim, Image.new("RGBA", (pw, ph), (0, 0, 0, 0)), rounded_mask((pw, ph), s(12))))
    return img


def chip(text, color, dot=None):
    font = ImageFont.truetype(FONT_BOLD, s(25))
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    tw = probe.textlength(text, font=font)
    extra = s(30) if dot else 0
    w, h = int(tw) + s(44) + extra, s(52)
    img = Image.new("RGBA", (w + s(30), h + s(30)), (0, 0, 0, 0))
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((s(15), s(21), s(15) + w, s(21) + h), radius=h // 2, fill=(0, 0, 0, 150))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(s(7))))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((s(15), s(15), s(15) + w, s(15) + h), radius=h // 2, fill=color + (255,), outline=(255, 255, 255, 90), width=s(2))
    x = s(15) + s(22)
    if dot:
        d.ellipse((x, s(15) + h // 2 - s(9), x + s(18), s(15) + h // 2 + s(9)), fill=dot + (255,))
        x += s(30)
    d.text((x, s(15) + h // 2), text, font=font, fill=(255, 255, 255, 255), anchor="lm")
    return img


def main():
    canvas = gradient((s(W), s(H)), hexrgb("#0b0e22"), hexrgb("#1a1238"))
    glow(canvas, (s(930), s(320)), s(440), PURPLE, 90)
    glow(canvas, (s(1150), s(620)), s(330), BLUE, 70)
    glow(canvas, (s(220), s(640)), s(300), BLUE, 40)

    dots = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    dd = ImageDraw.Draw(dots)
    for gx in range(0, s(W), s(32)):
        for gy in range(0, s(H), s(32)):
            dd.ellipse((gx, gy, gx + s(2), gy + s(2)), fill=(255, 255, 255, 14))
    canvas.alpha_composite(dots)

    # the wall of posters, tilted as a block
    wall = Image.new("RGBA", (s(720), s(760)), (0, 0, 0, 0))
    lit_index = 5
    for i, (top, bottom, motif) in enumerate(POSTERS):
        col, row = i % 4, i // 4
        p = poster(top, bottom, motif, lit=(i == lit_index))
        wall.alpha_composite(p, (s(10) + col * s(170), s(10) + row * s(242)))
    wall = wall.rotate(-9, resample=Image.BICUBIC, expand=True)
    wx, wy = s(560), s(-40)
    shadow = Image.new("RGBA", wall.size, (5, 5, 20, 0))
    shadow.putalpha(wall.split()[3].point(lambda a: a * 0.5))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(18))), (wx + s(12), wy + s(24)))
    canvas.alpha_composite(wall, (wx, wy))

    # the magnifier over the lit poster
    mx, my, mr = s(878), s(372), s(118)
    lens = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(lens)
    ld.ellipse((mx - mr, my - mr, mx + mr, my + mr), fill=(255, 255, 255, 34))
    canvas.alpha_composite(lens)
    ring = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    handle = [(mx + int(mr * 0.72), my + int(mr * 0.72)), (mx + s(250), my + s(250))]
    rd.line(handle, fill=(20, 18, 40, 255), width=s(58))
    rd.line(handle, fill=hexrgb("#00a4dc") + (255,), width=s(44))
    rd.ellipse((mx - mr - s(16), my - mr - s(16), mx + mr + s(16), my + mr + s(16)), outline=(20, 18, 40, 255), width=s(40))
    shade = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shade).ellipse((mx - mr - s(10), my - mr - s(10), mx + mr + s(10), my + mr + s(10)), outline=(255, 255, 255, 255), width=s(24))
    grad = gradient(canvas.size, PURPLE, BLUE, horizontal=True)
    ring.alpha_composite(Image.composite(grad, Image.new("RGBA", canvas.size, (0, 0, 0, 0)), shade.split()[3]))
    gloss = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(gloss).arc((mx - mr + s(22), my - mr + s(22), mx + mr - s(22), my + mr - s(22)), 200, 260, fill=(255, 255, 255, 170), width=s(10))
    sh = Image.new("RGBA", canvas.size, (5, 5, 20, 0))
    sh.putalpha(ring.split()[3].point(lambda a: a * 0.6))
    canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(s(14))), (s(8), s(20)))
    canvas.alpha_composite(ring)
    canvas.alpha_composite(gloss.filter(ImageFilter.GaussianBlur(s(2))))

    # filter chips
    canvas.alpha_composite(chip("IMDb > 8", hexrgb("#b8860b")), (s(600), s(78)))
    canvas.alpha_composite(chip("années 80", hexrgb("#7a3fa0")), (s(1010), s(150)))
    canvas.alpha_composite(chip("comédie OU drame", hexrgb("#0b6f9e")), (s(560), s(560)))
    canvas.alpha_composite(chip("MOVIES03", hexrgb("#1f3a5a"), dot=(46, 204, 113)), (s(1040), s(395)))

    # title
    d = ImageDraw.Draw(canvas)
    title = ImageFont.truetype(FONT_TITLE, s(112))
    sub = ImageFont.truetype(FONT_TEXT, s(36))
    d.text((s(84), s(300)), "Jelly", font=title, fill=(255, 255, 255, 255), anchor="ls")
    jw = d.textlength("Jelly", font=title)
    word_w = int(d.textlength("Search", font=title)) + s(10)
    tgrad = gradient((word_w, s(150)), PURPLE, BLUE, horizontal=True)
    mask = Image.new("L", tgrad.size, 0)
    ImageDraw.Draw(mask).text((0, s(122)), "Search", font=title, fill=255, anchor="ls")
    canvas.paste(tgrad, (int(s(84) + jw), s(300) - s(122)), mask)
    bar = gradient((s(120), s(8)), PURPLE, BLUE, horizontal=True)
    canvas.paste(bar, (s(88), s(334)), rounded_mask(bar.size, s(4)))
    d.text((s(86), s(410)), "Le bon film, même mal écrit.", font=sub, fill=(214, 218, 236, 255), anchor="ls")
    small = ImageFont.truetype(FONT_TEXT, s(26))
    d.text((s(86), s(460)), "Filtres, phrases et tuiles,", font=small, fill=(160, 166, 196, 255), anchor="ls")
    d.text((s(86), s(498)), "sur le web et la TV.", font=small, fill=(160, 166, 196, 255), anchor="ls")

    canvas.resize((W, H), Image.LANCZOS).convert("RGB").save(OUT, optimize=True)
    print("written", OUT)


if __name__ == "__main__":
    main()
