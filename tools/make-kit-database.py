"""Builds src/A2G.Convert/Data/club_kits.json from a club kit export (CSV without header, one club per line):

    python tools/make-kit-database.py <teams.csv>

Line layout (32 fields, as written by the FM kit colour export tool "Kit Color Studio"):
    0 name, 1 database id (= unique_id of fmsave's clubs table), 5 short name, 6 badge path,
    7-9 home shirt / shorts / socks, 10-12 away, 13-15 third (not used), 16-18 styles of home / away / third,
    19 abbreviation, 20-31 goalkeeper kits (not used).
Colours are "#RRGGBB" and are mapped to the 13 colours of Anstoss 2 (by brightness and hue family); #000000 means "no colour defined" and becomes white;
a kit whose three colours are all undefined is left out (the program then makes one up). Styles: Plain -> plain, Striped -> vertical stripes, Hooped -> hoops, Bicolour -> halves, Sleeved -> sleeves.

Output: {"kits": {"<id>": [home pattern, shirt, shorts, socks, away pattern, shirt, shorts, socks] or the 4 home values only}}
"""
import colorsys, csv, io, json, sys

STYLES = {"Plain": 0, "Striped": 1, "Hooped": 2, "Bicolour": 3, "Sleeved": 6}


def nearest(hexcolour):
    """The colour of the game closest to an FM colour: grey scale by brightness, the rest by hue family (a plain distance in a
    colour space maps mid blues to grey and dark reds to brown, because the game's 13 colours are so few)."""
    if hexcolour.upper() == "#000000":
        return 0                                    # "no colour defined" (FM's black is #080808) -> white
    r, g, b = (int(hexcolour[i:i + 2], 16) / 255.0 for i in (1, 3, 5))
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    hue = h * 360
    if v < 0.18:
        return 11                                   # black -> dark grey
    if s < 0.18:
        return 0 if v > 0.62 else 11                # white / silver -> white, dark greys -> dark grey
    if hue >= 345 or hue < 12:
        return 0 if (v > 0.85 and s < 0.5) else 1   # red (a very light pink -> white)
    if hue < 40:
        return 10 if v > 0.7 else 8                 # orange, dark orange -> brown
    if hue < 70:
        return 2 if v >= 0.5 else 8                 # yellow / gold
    if hue < 100:
        return 4                                    # yellow green
    if hue < 160:
        return 4 if v > 0.7 else 5                  # bright green / dark green
    if hue < 190:
        return 6                                    # teal
    if hue < 250:
        return 7 if (hue < 205 or (v > 0.85 and s < 0.7)) else 3   # light blue / blue
    if hue < 320:
        return 9                                    # purple
    return 12 if v < 0.7 else 9 if hue < 330 else 12   # claret / pink-red


def kit(colours, style):
    """colours: three '#RRGGBB' (shirt, shorts, socks); None when the kit has no data."""
    if all(c.upper() == "#000000" for c in colours):
        return None
    shirt, shorts, socks = (nearest(c) for c in colours)
    pattern = STYLES.get(style, 0)
    if pattern != 0 and shirt == shorts:
        pattern = 0   # the game draws the stripes in the colour of the shorts: equal colours would show nothing
    return [pattern, shirt, shorts, socks]


def main():
    source, target = sys.argv[1], "src/A2G.Convert/Data/club_kits.json"
    kits = {}
    for row in csv.reader(io.open(source, encoding="utf-8-sig")):
        if len(row) < 19 or not row[1].isdigit():
            continue
        home = kit(row[7:10], row[16])
        if home is None:
            continue
        away = kit(row[10:13], row[17])
        kits[row[1]] = home + (away or [])
    with io.open(target, "w", encoding="utf-8", newline="\n") as out:
        out.write('{"kits":{')
        out.write(",".join('"%s":[%s]' % (k, ",".join(map(str, v))) for k, v in sorted(kits.items(), key=lambda kv: int(kv[0]))))
        out.write("}}\n")
    print("%d clubs, %d with an away kit" % (len(kits), sum(1 for v in kits.values() if len(v) == 8)))


main()
