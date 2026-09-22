#!/usr/bin/env python3
"""Draws src/WindowsSimpleTaskTabBar/Properties/app.ico.

The icon is a square box with tabs stacked inside it, each one stepped down and to the right
of the one behind, the way overlapping windows sit on a screen. The box is a blue outline.
The tab in front is filled white, as the active tab is on the bar; the ones behind it carry
the pale shade of an inactive tab, and each is partly hidden by the one in front of it.

Everything is drawn in straight lines on whole pixels, so no pixel is ever part-transparent
and nothing is ever blended. That is what keeps the icon sharp at 16 pixels, which is the
size the taskbar and the notification area ask for.

Three tabs at 16 and 20 pixels would leave two pixels of each tab behind showing, which
reads as a blue smudge rather than as a stack, so those two sizes carry two tabs and the
rest carry three. Nobody sees two sizes at once, and legibility at the size it is actually
looked at wins.

Why this is a script rather than a drawing saved from an editor: an icon exported from a
vector drawing lands its edges between pixels, and the renderer then spreads each one over
two columns, which is what makes a small icon look soft. The proportions also have to be
held per size rather than scaled from one drawing: an outline one pixel wide at 16x16 has
to stay one pixel, not two thirds of one.

Run it from anywhere; it writes over the icon in place:

    python3 tools/make-app-icon.py

It needs the standard library alone, and it is run by hand when the icon changes rather
than by the build. Nothing in the build depends on it.
"""

import os
import struct
import zlib

BLUE = (0x25, 0x63, 0xEB)    # the box, and the outline of every tab
WHITE = (0xFF, 0xFF, 0xFF)   # the tab in front
PALE = (0x9D, 0xB9, 0xF6)    # a tab behind it

SUPERSAMPLE = 8

# The sizes Windows asks for: the notification area and the taskbar take the first three,
# the Start menu and the file list take the last two. All five are stored uncompressed,
# which is what every reader of an icon understands, and the five together are about 19 KB
# inside an executable the README keeps under 200 KB.
#
# Per size, in whole pixels:
#   frame   how far the box sits in from the edge of the icon
#   edge    how thick the box's outline is
#   side    how wide and tall a tab is
#   step    how far each tab is stepped down and to the right of the one behind it
#   stroke  how thick a tab's outline is
#   start   where the tab at the back begins, measured from the edge of the icon
#   count   how many tabs there are
#
# `start` has to clear the box's own outline, or the two run into each other and read as
# one thick line. `start + (count - 1) * step + side` has to stay inside it at the other
# end, for the same reason.
GEOMETRY = {
    16: dict(frame=0, edge=1, side=8, step=4, stroke=1, start=2, count=2),
    20: dict(frame=0, edge=1, side=11, step=5, stroke=1, start=2, count=2),
    24: dict(frame=1, edge=1, side=10, step=4, stroke=1, start=3, count=3),
    32: dict(frame=1, edge=2, side=14, step=5, stroke=2, start=4, count=3),
    48: dict(frame=2, edge=2, side=22, step=7, stroke=2, start=6, count=3),
}


def rectangle(left, top, right, bottom):
    """A filled rectangle."""
    return lambda x, y: left <= x < right and top <= y < bottom


def outline(left, top, right, bottom, thickness):
    """A rectangle's outline, drawn inside its bounds."""
    def covers(x, y):
        if not (left <= x < right and top <= y < bottom):
            return False
        return not (left + thickness <= x < right - thickness
                    and top + thickness <= y < bottom - thickness)
    return covers


def layers(size):
    """What the icon is made of at this size, in the order it is painted."""
    g = GEOMETRY[size]
    frame, count = g['frame'], g['count']
    shapes = [(outline(frame, frame, size - frame, size - frame, g['edge']), BLUE)]

    # Back to front, so each tab hides the part of the one behind it that it covers.
    for i in range(count):
        at = g['start'] + i * g['step']
        far = at + g['side']
        fill = WHITE if i == count - 1 else PALE
        shapes.append((rectangle(at, at, far, far), fill))
        shapes.append((outline(at, at, far, far, g['stroke']), BLUE))
    return shapes


def render(size):
    """The icon at this size, as a list of (r, g, b, a) rows top down."""
    shapes = layers(size)
    step = 1.0 / SUPERSAMPLE
    samples = SUPERSAMPLE * SUPERSAMPLE
    pixels = []
    for y in range(size):
        for x in range(size):
            red = green = blue = covered = 0
            for sy in range(SUPERSAMPLE):
                py = y + (sy + 0.5) * step
                for sx in range(SUPERSAMPLE):
                    px = x + (sx + 0.5) * step
                    colour = None
                    for covers, c in shapes:
                        if covers(px, py):
                            colour = c
                    if colour is not None:
                        red += colour[0]
                        green += colour[1]
                        blue += colour[2]
                        covered += 1
            if covered == 0:
                pixels.append((0, 0, 0, 0))
            else:
                pixels.append((int(round(red / covered)), int(round(green / covered)),
                               int(round(blue / covered)),
                               int(round(255.0 * covered / samples))))
    return pixels


def frame(size, pixels):
    """One image inside the icon: a 32-bit bitmap, bottom up, with its 1-bit mask after it."""
    body = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = pixels[y * size + x]
            body += bytes((b, g, r, a))
    colours = len(body)

    mask_stride = ((size + 31) // 32) * 4
    for y in range(size - 1, -1, -1):
        row = bytearray(mask_stride)
        for x in range(size):
            if pixels[y * size + x][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        body += row

    # A BITMAPINFOHEADER whose height covers the image and the mask together. Its size field
    # counts the colours alone, which is what every other icon carries.
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, colours, 0, 0, 0, 0)
    return header + bytes(body)


def icon(sizes):
    frames = [(size, frame(size, render(size))) for size in sizes]
    directory = bytearray()
    images = bytearray()
    offset = 6 + 16 * len(frames)
    for size, body in frames:
        directory += struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(body), offset)
        images += body
        offset += len(body)
    return struct.pack('<HHH', 0, 1, len(frames)) + bytes(directory) + bytes(images)


def main():
    target = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                          'src', 'WindowsSimpleTaskTabBar', 'Properties', 'app.ico')
    data = icon(sorted(GEOMETRY))
    with open(target, 'wb') as f:
        f.write(data)
    print('wrote {} ({} bytes, {} sizes)'.format(target, len(data), len(GEOMETRY)))


if __name__ == '__main__':
    main()
