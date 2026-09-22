#!/usr/bin/env python3
"""Draws src/WindowsSimpleTaskTabBar/Properties/app.ico.

The icon is the bar: a blue strip, wider than it is tall, with rounded corners, carrying two
tabs. Both tabs are the same size, as they are on screen, and neither is taller than it is
wide. Each is rounded at the top, square at the bottom, and stands on the bottom edge of the
strip. The left tab is the active one and is filled white; the right one carries the pale
shade of an inactive tab. A thin gap of blue separates them.

The strip is not square, and does not fill the frame: it is a bar, and the space above and
below it is transparent. How tall it can be follows from the tabs. Two of them side by side
leave each a little under half the width, and a tab is never taller than it is wide, so the
strip comes to a little under half the height of the frame.

Why this is a script rather than a drawing saved from an editor:

* Every edge is placed on a whole pixel, at every size. An icon exported from a vector
  drawing lands its edges between pixels, and the renderer then spreads each one over two
  columns, which is what makes a small icon look soft. Here the only part-transparent pixels
  are on the four corners of the strip and the two top corners of each tab: 12 pixels of 256
  at 16x16.
* The proportions are held per size rather than scaled from one drawing. At 16x16 a tab is
  6 pixels square and the gap between the two is 2, and all of those have to stay whole
  numbers.

Run it from anywhere; it writes over the icon in place:

    python3 tools/make-app-icon.py

It needs the standard library alone, and it is run by hand when the icon changes rather
than by the build. Nothing in the build depends on it.
"""

import os
import struct
import zlib

BLUE = (0x25, 0x63, 0xEB)    # the bar
WHITE = (0xFF, 0xFF, 0xFF)   # the active tab
PALE = (0x9D, 0xB9, 0xF6)    # an inactive tab

SUPERSAMPLE = 8

GAP = 2  # blue between the two tabs, at every size

# The sizes Windows asks for: the notification area and the taskbar take the first three,
# the Start menu and the file list take the last two. All five are stored uncompressed,
# which is what every reader of an icon understands, and the five together are about 19 KB
# inside an executable the README keeps under 200 KB.
#
# Per size, in whole pixels:
#   margin  the blue to the left of the first tab, to the right of the second, and above
#           both. A tab is (size - 2 * margin - GAP) / 2 wide and that many tall, and the
#           strip is one tab plus one margin tall.
#   radius  the four corners of the strip
#   corner  the two top corners of a tab
#
# `margin` also has to keep a tab clear of the strip's own rounded corner, which reaches
# 0.29 * radius in from the edge along the bottom row. A tab that crossed it would be cut
# by the clip below and lose its square bottom corner.
#
# GAP is 2 rather than 1 because `size` is even: an odd gap cannot leave two tabs of equal
# whole-pixel width.
GEOMETRY = {
    16: dict(margin=1, radius=2, corner=1),
    20: dict(margin=1, radius=3, corner=2),
    24: dict(margin=2, radius=3, corner=2),
    32: dict(margin=2, radius=4, corner=3),
    48: dict(margin=3, radius=6, corner=4),
}


def rounded(left, top, right, bottom, radius, top_only=False):
    """A rectangle with rounded corners; with top_only, the bottom two stay square."""
    def covers(x, y):
        if x < left or x >= right or y < top or y >= bottom:
            return False
        if radius <= 0:
            return True
        if y < top + radius:
            if x < left + radius:
                dx, dy = (left + radius) - x, (top + radius) - y
                return dx * dx + dy * dy <= radius * radius
            if x > right - radius:
                dx, dy = x - (right - radius), (top + radius) - y
                return dx * dx + dy * dy <= radius * radius
        if not top_only and y > bottom - radius:
            if x < left + radius:
                dx, dy = (left + radius) - x, y - (bottom - radius)
                return dx * dx + dy * dy <= radius * radius
            if x > right - radius:
                dx, dy = x - (right - radius), y - (bottom - radius)
                return dx * dx + dy * dy <= radius * radius
        return True
    return covers


def layers(size):
    """What the icon is made of at this size, in the order it is painted."""
    g = GEOMETRY[size]
    margin = g['margin']
    tab = (size - 2 * margin - GAP) // 2   # a tab is this wide and this tall
    strip = tab + margin
    top = (size - strip) // 2              # the bar sits in the middle of the frame
    bottom = top + strip

    bar = rounded(0, top, size, bottom, g['radius'])

    def on_the_bar(shape):
        # A tab cannot reach past the bar it stands on, so it is clipped to it.
        return lambda x, y: bar(x, y) and shape(x, y)

    left_tab = margin
    right_tab = margin + tab + GAP
    return [
        (bar, BLUE),
        (on_the_bar(rounded(left_tab, top + margin, left_tab + tab, bottom, g['corner'],
                            top_only=True)), WHITE),
        (on_the_bar(rounded(right_tab, top + margin, right_tab + tab, bottom, g['corner'],
                            top_only=True)), PALE),
    ]


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
