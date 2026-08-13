#!/usr/bin/env python3
"""Draws this mod's icon: the branch glyph over Background_for_icon.jpg.

    python3 tools/make_icon.py
    python3 tools/make_icon.py --preview      # also a 1024px look at it

Writes into GitView/Resources:

    icon.png    256px, Mod.xml's <Icon> -- the logo in the game's mods menu
    thumb.png   512px, Mod.xml's <WorkshopThumbnail>

The glyph is the one the mod draws on the load screen's branch buttons, constant
for constant out of IconArt.cs: same trunk, same quarter-turn arc, same three
commits. The three commits carry what the mod is for -- a green plus, a yellow
tilde and a red minus, in the colours DiffPalette starts a session with, so the
icon and the machine on screen are saying the same thing in the same colours.

The commits are the mod's own solid white discs. What holds the marks on them is
a thin black edge drawn around each one: a yellow tilde on white is not a tilde
anybody can see, and all three marks are edged the same way or the icon looks
like an accident.
"""

import argparse
import math
import os
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("This needs Pillow: pip install --user Pillow")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BACKGROUND = os.path.join(REPO, "Background_for_icon.jpg")
RESOURCES = os.path.join(REPO, "GitView", "Resources")

# Everything is drawn at this multiple and scaled down at the end, which is how
# the edges get their anti-aliasing. IconArt does the same thing by sampling four
# by four inside every pixel.
SUPERSAMPLE = 4

# The glyph, in a unit square with y running up -- IconArt.cs, "the shape".
TRUNK_X = 0.30
BRANCH_X = 0.72
BOTTOM_Y = 0.14
TOP_Y = 0.86
STROKE = 0.075

# Wider than IconArt's 0.125. There the disc is a solid dot; here it is a badge
# with a mark inside it, and at the mod's radius the mark is four pixels of a
# 256px icon.
NODE_RADIUS = 0.165

# How much of the disc the mark inside it spans, how heavy it is drawn, and how
# far the black edge stands out past it each side. The edge is what lets a yellow
# tilde be seen at all on a white disc; the other two would read without it, and
# have it anyway so the three are one set.
MARK_SPAN = 0.62
MARK_STROKE = 0.030
MARK_EDGE = 0.0075

# How much of the icon the glyph is drawn across, and where its box sits in the
# square. Nudged right, because the box is not the shape: the trunk runs down the
# left and the only thing out to the right is one commit, so a glyph centred by
# its box is a glyph that looks pushed into the left-hand side.
GLYPH_SPAN = 0.78
GLYPH_SHIFT = 0.035
GLYPH_LIFT = -0.01

# DiffPalette.Fallbacks, opacity dropped: these are marks on an icon, not shells
# over a machine.
ADDED = (0, 255, 0, 255)
CHANGED = (255, 255, 0, 255)
REMOVED = (254, 0, 0, 255)

WHITE = (255, 255, 255, 255)

# What the edge around a mark is drawn in. The plate the mod draws its own icons
# on (IconArt.PlateInk) rather than a flat black, so the icon is drawn out of the
# same two shades as everything else here.
MARK_INK = (2, 7, 13, 255)

# The rounded frame: how far in the rim sits, how round the corners are, and how
# heavy the darkening at the edges is.
CORNER = 0.10
RIM = (255, 255, 255, 46)
RIM_WIDTH = 0.008
VIGNETTE = 90


def to_pixels(size):
    """Answers a function from unit-square glyph coordinates to image pixels."""
    span = size * GLYPH_SPAN
    left = (size - span) * 0.5 + size * GLYPH_SHIFT
    # y runs up in the glyph's own coordinates and down in the image's.
    top = (size - span) * 0.5 + size * GLYPH_LIFT

    def at(u, v):
        return (left + u * span, top + (1.0 - v) * span)

    return at, span


def draw_glyph(draw, size):
    """The trunk, the arc and the three commits, with a mark on each commit."""
    point, span = to_pixels(size)
    stroke = STROKE * span
    radius = NODE_RADIUS * span

    # The trunk, commit to commit. Its ends are inside the discs at either end,
    # so nothing has to be done about the shape of them.
    draw.line([point(TRUNK_X, BOTTOM_Y), point(TRUNK_X, TOP_Y)],
              fill=WHITE, width=int(round(stroke)))

    # The branch: a quarter turn centred on the *top of the trunk*, leaving the
    # trunk horizontally and arriving at its own commit vertically.
    arc_r = (BRANCH_X - TRUNK_X) * span
    cx, cy = point(TRUNK_X, TOP_Y)
    draw.arc([cx - arc_r, cy - arc_r, cx + arc_r, cy + arc_r],
             start=0, end=90, fill=WHITE, width=int(round(stroke)))

    nodes = [(TRUNK_X, TOP_Y, ADDED, "plus"),
             (BRANCH_X, TOP_Y, CHANGED, "tilde"),
             (TRUNK_X, BOTTOM_Y, REMOVED, "minus")]
    for u, v, colour, mark in nodes:
        x, y = point(u, v)
        draw.ellipse([x - radius, y - radius, x + radius, y + radius],
                     fill=WHITE)
        reach = radius * MARK_SPAN
        weight = MARK_STROKE * span
        # The edge first and the colour over it, so what is left of the black is
        # the amount that stood out past the mark each side.
        draw_mark(draw, mark, x, y, reach, weight + MARK_EDGE * span * 2,
                  MARK_INK)
        draw_mark(draw, mark, x, y, reach, weight, colour)


def draw_mark(draw, mark, x, y, reach, weight, colour):
    """One of +, ~ and - inside a node, drawn to the same weight and reach so
    the three read as one set."""
    width = max(1, int(round(weight)))

    def stroke(points):
        draw.line(points, fill=colour, width=width, joint="curve")
        # Round the ends, which a polyline leaves square.
        radius = width * 0.5
        for end in (points[0], points[-1]):
            draw.ellipse([end[0] - radius, end[1] - radius,
                          end[0] + radius, end[1] + radius], fill=colour)

    if mark in ("plus", "minus"):
        stroke([(x - reach, y), (x + reach, y)])
    if mark == "plus":
        stroke([(x, y - reach), (x, y + reach)])
    if mark == "tilde":
        # A sine wave one period wide, sampled rather than curved: two arcs
        # joined at the middle leave a corner where they meet.
        steps = 48
        stroke([(x - reach + 2 * reach * (i / float(steps)),
                 y - math.sin(i / float(steps) * 2 * math.pi) * reach * 0.42)
                for i in range(steps + 1)])


def background(size):
    """The photograph, cropped square and covering the canvas."""
    image = Image.open(BACKGROUND).convert("RGBA")
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    return image.crop((left, top, left + side, top + side)).resize(
        (size, size), Image.LANCZOS)


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1],
                                           radius=radius, fill=255)
    return mask


def vignette(size, strength):
    """Darkens the edges, so a white glyph has something to sit against
    wherever the photograph happens to be pale.

    Drawn small and scaled up rather than drawn at full size: a stack of
    ellipses is a stack of hard edges, and at icon size those read as rings.
    Resampling from a sixty-fourth of the canvas turns them into a gradient."""
    coarse = 64
    shade = Image.new("L", (coarse, coarse), strength)
    draw = ImageDraw.Draw(shade)
    steps = 32
    # Darkest first and largest first: each ellipse is a little smaller and a
    # little lighter than the one under it, ending clear in the middle. The
    # first is bigger than the canvas, or the corners keep the flat fill.
    for i in range(steps):
        far = i / float(steps)
        inset = coarse * (-0.20 + 0.70 * far)
        draw.ellipse([inset, inset, coarse - 1 - inset, coarse - 1 - inset],
                     fill=int(strength * (1 - far) ** 1.6))
    return shade.resize((size, size), Image.BICUBIC)


def build(size):
    big = size * SUPERSAMPLE
    canvas = background(big)

    dark = Image.new("RGBA", (big, big), (0, 8, 20, 255))
    canvas = Image.composite(dark, canvas, vignette(big, VIGNETTE))

    layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw_glyph(ImageDraw.Draw(layer), big)
    canvas = Image.alpha_composite(canvas, layer)

    frame = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(
        [0, 0, big - 1, big - 1], radius=int(CORNER * big), outline=RIM,
        width=max(1, int(RIM_WIDTH * big)))
    canvas = Image.alpha_composite(canvas, frame)

    canvas.putalpha(rounded_mask(big, int(CORNER * big)))
    return canvas.resize((size, size), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true",
                        help="also write a 1024px preview beside the icons")
    args = parser.parse_args()

    if not os.path.isfile(BACKGROUND):
        sys.exit("No %s to draw on." % BACKGROUND)
    if not os.path.isdir(RESOURCES):
        os.makedirs(RESOURCES)

    wanted = [(os.path.join(RESOURCES, "icon.png"), 256),
              (os.path.join(RESOURCES, "thumb.png"), 512)]
    if args.preview:
        # Outside Resources, which the mod folder ships whole: a preview is for
        # looking at while working on this, not for installing.
        wanted.append((os.path.join(REPO, "icon-preview.png"), 1024))
    for path, size in wanted:
        build(size).save(path)
        print("wrote %s (%dpx)" % (path, size))


if __name__ == "__main__":
    main()
