#!/usr/bin/env python3
"""Draws src/WindowsSimpleTaskTabBar/Properties/app.ico.

The icon is the bar itself, filled blue with rounded corners, carrying two tabs. Both tabs
are the same size, as they are on screen, and both are wider than they are tall. Each is
rounded at the top, square at the bottom and stands on the bottom edge of the bar. The left
tab is the active one and is filled white; the right one carries the pale shade of an
inactive tab.

Why this is a script rather than a drawing saved from an editor:

* Every edge is placed on a whole pixel, at every size. An icon exported from a vector
  drawing lands its edges between pixels, and the renderer then spreads each one over two
  columns, which is what makes a small icon look soft. Here the only part-transparent pixels
  are on the four corners of the bar and the two top corners of each tab: 12 pixels of 256
  at 16x16.
* The proportions are held per size rather than scaled from one drawing. At 16x16 a tab is
  6 pixels wide and 5 tall, and both have to stay whole numbers.

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

# The sizes Windows asks for: the notification area and the taskbar take the first three,
# the Start menu and the file list take the last two. All five are stored uncompressed,
# which is what every reader of an icon understands, and the five together are about 19 KB
# inside an executable the README keeps under 200 KB.
#
# Per size, in whole pixels:
#   pad     the blue left of the first tab, right of the second, and between the two,
#           which is twice this. A tab is therefore (size - 4 * pad) / 2 wide.
#   tab     the height of a tab, which is always less than its width
#   radius  the four corners of the bar
#   corner  the two top corners of a tab
#
# `pad` also has to keep a tab clear of the bar's own rounded corner, which reaches
# 0.29 * radius in from the edge along the bottom row. A tab that crossed it would be cut
# by the clip below and lose its square bottom corner.
GEOMETRY = {
    16: dict(pad=1, tab=5, radius=2, corner=1),
    20: dict(pad=1, tab=7, radius=3, corner=2),
    24: dict(pad=1, tab=9, radius=3, corner=2),
    32: dict(pad=2, tab=11, radius=4, corner=3),
    48: dict(pad=3, tab=17, radius=6, corner=4),
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
    pad, height = g['pad'], g['tab']
    gap = 2 * pad
    width = (size - 4 * pad) // 2
    top = size - height
    left_tab = pad
    right_tab = pad + width + gap

    bar = rounded(0, 0, size, size, g['radius'])

    def on_the_bar(shape):
        # A tab cannot reach past the bar it sits on, so it is clipped to it.
        return lambda x, y: bar(x, y) and shape(x, y)

    return [
        (bar, BLUE),
        (on_the_bar(rounded(left_tab, top, left_tab + width, size, g['corner'],
                            top_only=True)), WHITE),
        (on_the_bar(rounded(right_tab, top, right_tab + width, size, g['corner'],
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
