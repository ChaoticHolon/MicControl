# Builds MicControl's icon font from Microsoft's Fluent UI System Icons (MIT licence, the same design language as
# Windows 11's Segoe Fluent Icons), so the icons look the same on Windows 10, Windows 11 and Linux.
# Run from the repository root:  python tools/GenerateIconFont.py   (needs: pip install fonttools)
#
# The font keeps only the icons the UI uses and puts each one at the Segoe Fluent Icons code point the XAML already
# uses (e.g. U+E720 is the microphone), so XAML keeps writing "&#xE720;". To add an icon: pick its Fluent name at
# https://github.com/microsoft/fluentui-system-icons, add the Segoe code point and the name to ICONS, run this again,
# and commit the font. Never edit the font by hand.
#
# Outputs: Sources/MicControl.UI/Assets/Fonts/MicControlIcons.ttf and its LICENSE.txt.

import io
import json
import os
import urllib.request

from fontTools import subset
from fontTools.ttLib import TTFont

# The upstream commit the icons come from; change it on purpose, to pick up new or redrawn icons.
SOURCE_COMMIT = "a347af95478f989038cccbf085978865264c048a"
SOURCE = f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{SOURCE_COMMIT}"
FAMILY = "MicControl Icons"
OUTPUT = os.path.join("Sources", "MicControl.UI", "Assets", "Fonts")

# Segoe Fluent Icons code point (what the XAML uses) -> Fluent UI System Icons name.
ICONS = {
    0xE80F: "home",               # Home tab
    0xE720: "mic",                # live
    0xEC54: "mic_off",            # muted
    0xE995: "speaker_2",          # Sounds tab, Play sounds
    0xE767: "speaker_2",          # live speakers, input volume
    0xE74F: "speaker_mute",       # muted speakers
    0xE994: "speaker_1",          # sound volume
    0xE7F5: "speaker_settings",   # play through
    0xE768: "play",               # preview a sound
    0xE7F4: "desktop",            # Overlay tab, show overlay
    0xE765: "keyboard",           # Custom hotkeys tab, hotkey rows
    0xE946: "info",               # About tab, status messages
    0xE7BA: "warning",            # limitation notice
    0xE711: "dismiss",            # dismiss a message
    0xE710: "add",                # add a hotkey or sound
    0xE74D: "delete",             # remove a row
    0xE742: "arrow_up_left",      # overlay corner (flipped for the other corners)
    0xE8B0: "arrow_move",         # overlay position
    0xE740: "resize",             # overlay size
    0xE790: "color",              # opacity, theme
    0xE8E9: "text_font",          # Muted / Live label
    0xEB9F: "image",              # custom overlay icons
    0xE7E8: "power",              # start with Windows, state at startup
    0xE8AB: "arrow_swap",         # mode
    0xE73F: "window",             # app mode
    0xE72E: "lock_closed",        # hotkey access
    0xE895: "arrow_sync",         # check for updates
    0xE823: "clock",              # check automatically
    0xE7F8: "laptop",             # operating system
    0xE943: "code",               # runtime
    0xE771: "paint_brush",        # UI framework
    0xE8B7: "folder",             # settings folder
    0xE9F9: "document_text",      # logs
    0xE9D9: "options",            # log detail
    0xE7BE: "hat_graduation",     # setup guide
}

# 20 px drawings suit the UI's 13-20 px icons best; fall back to 24 px where an icon has no 20 px drawing.
SIZES = (20, 24)

# How much larger than upstream to draw each icon (see the centring step in main).
SCALE = 1.2


def fetch(path):
    with urllib.request.urlopen(f"{SOURCE}/{path}") as response:
        return response.read()


def main():
    names = json.loads(fetch("fonts/FluentSystemIcons-Regular.json"))
    font = TTFont(io.BytesIO(fetch("fonts/FluentSystemIcons-Regular.ttf")))
    upstream = font.getBestCmap()

    # Segoe code point -> upstream glyph name.
    glyphs = {}
    for codepoint, icon in ICONS.items():
        source = next((names[k] for k in (f"ic_fluent_{icon}_{s}_regular" for s in SIZES) if k in names), None)
        if source is None:
            raise SystemExit(f"No Fluent icon named {icon!r}")
        glyphs[codepoint] = upstream[source]

    options = subset.Options()
    options.layout_features = []
    options.name_IDs = []
    options.notdef_outline = True
    options.glyph_names = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(glyphs=sorted(set(glyphs.values())))
    subsetter.subset(font)

    # Upstream glyphs sit at the left edge of their box and use 80% of its height (the 20 px grid keeps 2 px of
    # padding). Centre each one and scale it to fill about as much of the box as Segoe Fluent Icons does.
    upem = font["head"].unitsPerEm
    glyf = font["glyf"]
    for glyph_name in set(glyphs.values()):
        glyph = glyf[glyph_name]
        if glyph.isComposite() or glyph.numberOfContours == 0:
            continue
        glyph.recalcBounds(glyf)
        centre_x, centre_y = (glyph.xMin + glyph.xMax) / 2, upem / 2
        glyph.coordinates.transform(((SCALE, 0), (0, SCALE)))
        glyph.coordinates.translate((round(upem / 2 - centre_x * SCALE), round(centre_y - centre_y * SCALE)))
        glyph.coordinates.toInt()
        glyph.recalcBounds(glyf)
        font["hmtx"][glyph_name] = (upem, glyph.xMin)

    # Point each Segoe code point at its glyph, and nothing else.
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap = dict(glyphs)
    font["cmap"].tables = [t for t in font["cmap"].tables if t.isUnicode()]

    name = font["name"]
    name.names = []
    for name_id, value in {
        0: "Icons: Copyright (c) 2020 Microsoft Corporation (Fluent UI System Icons, MIT licence)",
        1: FAMILY,
        2: "Regular",
        3: f"{FAMILY} Regular; fluentui-system-icons {SOURCE_COMMIT[:7]}",
        4: f"{FAMILY} Regular",
        5: "Version 1.0",
        6: FAMILY.replace(" ", "") + "-Regular",
    }.items():
        name.setName(value, name_id, 3, 1, 0x409)

    os.makedirs(OUTPUT, exist_ok=True)
    font.save(os.path.join(OUTPUT, "MicControlIcons.ttf"))
    with open(os.path.join(OUTPUT, "LICENSE.txt"), "wb") as licence:
        licence.write(b"MicControlIcons.ttf contains icons from Fluent UI System Icons\n")
        licence.write(f"(https://github.com/microsoft/fluentui-system-icons, commit {SOURCE_COMMIT}),\n".encode())
        licence.write(b"remapped to Segoe Fluent Icons code points. Their licence follows.\n\n")
        licence.write(fetch("LICENSE"))
    print(f"{len(ICONS)} icons -> {os.path.join(OUTPUT, 'MicControlIcons.ttf')}")


if __name__ == "__main__":
    main()
