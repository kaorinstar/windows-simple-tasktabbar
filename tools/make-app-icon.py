#!/usr/bin/env python3
"""Draws src/WindowsSimpleTaskTabBar/Properties/app.ico.

The icon is two tabs standing on the bar: rounded at the top, square at the bottom, and
flush with the bottom edge of the frame, which is the shape the application draws its own
tabs in. The left tab is the active one and is filled white; the right one is filled in the
pale shade an inactive tab carries.

Why this is a script rather than a drawing saved from an editor:

* Every edge is placed on a whole pixel, at every size. An icon exported from a vector
  drawing lands its edges between pixels, and the renderer then spreads each one over two
  columns, which is what makes a small icon look soft. Here only the four top corners of
  each tab are ever part-transparent: 4 pixels of 256 at 16x16.
* The proportions are held per size rather than scaled from one drawing. A border one pixel
  wide at 16x16 has to stay one pixel, not two thirds of one.

Run it from anywhere; it writes over the icon in place:

    python3 tools/make-app-icon.py

It needs the standard library alone, and it is run by hand when the icon changes rather
than by the build. Nothing in the build depends on it.
"""

import os
import struct
import zlib

BLUE = (0x25, 0x63, 0xEB)    # the bar, and the outline of every tab
WHITE = (0xFF, 0xFF, 0xFF)   # the fill of the active tab
PALE = (0x9D, 0xB9, 0xF6)    # the fill of an inactive tab

SUPERSAMPLE = 8

# The sizes Windows asks for: the notification area and the taskbar take the first three,
# the Start menu and the file list take the last two. All five are stored uncompressed,
# which is what every reader of an icon understands, and the five together are about 19 KB
# inside an executable the README keeps under 200 KB.
#
# Per size, in whole pixels:
#   margin    the space left and right of the bar
#   gap       between the two tabs
#   bar       the height of the bar the tabs stand on
#   tab       the height of a tab above the bar
#   radius    the two top corners of a tab
#   border    the outline of a tab, inside which it is filled
GEOMETRY = {
    16: dict(margin=1, gap=1, bar=2, tab=9, radius=1, border=1),
    20: dict(margin=1, gap=1, bar=2, tab=11, radius=2, border=1),
    24: dict(margin=2, gap=2, bar=3, tab=13, radius=2, border=1),
    32: dict(margin=2, gap=2, bar=4, tab=17, radius=3, border=2),
    48: dict(margin=2, gap=2, bar=5, tab=24, radius=4, border=2),
}

# The active tab is the wider of the two, as it is on screen.
ACTIVE_SHARE = 0.54


def tab_shape(left, top, right, bottom, radius):
    """A rectangle with its two top corners rounded and its bottom left square."""
    def covers(x, y):
        if x < left or x >= right or y < top or y >= bottom:
            return False
        if radius <= 0 or y >= top + radius:
            return True
        if x < left + radius:
            dx, dy = (left + radius) - x, (top + radius) - y
            return dx * dx + dy * dy <= radius * radius
        if x > right - radius:
            dx, dy = x - (right - radius), (top + radius) - y
            return dx * dx + dy * dy <= radius * radius
        return True
    return covers


def band(left, top, right, bottom):
    """A plain rectangle."""
    return lambda x, y: left <= x < right and top <= y < bottom


def layers(size):
    """What the icon is made of at this size, in the order it is painted."""
    g = GEOMETRY[size]
    margin, gap, border = g['margin'], g['gap'], g['border']
    width = size - 2 * margin
    shared = width - gap
    active = int(round(shared * ACTIVE_SHARE))
    inactive = shared - active

    bar_top = size - g['bar']
    top = bar_top - g['tab']
    left_a, right_a = margin, margin + active
    left_b, right_b = right_a + gap, right_a + gap + inactive
    inner_radius = max(0, g['radius'] - border)

    return [
        # Both tabs run to the bottom edge, so that the bar and the tabs are one shape.
        (tab_shape(left_a, top, right_a, size, g['radius']), BLUE),
        (tab_shape(left_b, top, right_b, size, g['radius']), BLUE),
        (band(margin, bar_top, size - margin, size), BLUE),
        # The fill of each tab stops at the bar, which stays solid across its whole width.
        (tab_shape(left_a + border, top + border, right_a - border, bar_top, inner_radius), WHITE),
        (tab_shape(left_b + border, top + border, right_b - border, bar_top, inner_radius), PALE),
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
