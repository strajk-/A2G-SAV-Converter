"""Entry point of the bundled fmsave.exe.

Everything fmsave offers still works unchanged (``fmsave.exe export ...``). On top of that this wrapper adds
commands that read the *installed* Football Manager database, which saves do not contain:

    fmsave.exe a2g-nations --fm-install "<folder of Football Manager 26>" -o nation_names.csv

writes ``database_id,name,short_name,code,demonym`` for every nation. The ``database_id`` is the id that
``fmsave export nations`` reports, so nation names can be joined to save data without any maintained table.
"""

from __future__ import annotations

import csv
import re
import struct
import sys
from pathlib import Path

from compression import zstd

ZSTD_MAGIC = b"\x28\xb5\x2f\xfd"
MAX_FRAME_BYTES = 512 * 1024 * 1024


def _find_language_db(install: Path) -> Path:
    db_root = install / "shared" / "data" / "database" / "db"
    candidates = []
    for version_dir in db_root.iterdir() if db_root.is_dir() else []:
        match = re.fullmatch(r"(\d+)", version_dir.name)
        if not match:
            continue
        for path in version_dir.glob("*_fm/lang_update_db.dat"):
            candidates.append((int(match.group(1)), path))
    if not candidates:
        raise SystemExit(f"fmsave: no lang_update_db.dat found under {db_root}")
    return max(candidates)[1]


def _read_nations(path: Path) -> list[tuple[int, str, str, str, str]]:
    data = path.read_bytes()
    start = data.find(ZSTD_MAGIC)
    if start < 0:
        raise SystemExit(f"fmsave: {path.name} holds no compressed frame")
    decompressor = zstd.ZstdDecompressor()
    frame = decompressor.decompress(data[start:], max_length=MAX_FRAME_BYTES)
    if len(frame) < 12:
        raise SystemExit("fmsave: nation table is too short")
    count = struct.unpack_from("<I", frame, 8)[0]
    position = 12

    def read_text(terminated: bool) -> str:
        nonlocal position
        length = struct.unpack_from("<I", frame, position)[0]
        position += 4
        text = frame[position : position + length].decode("utf-8", errors="replace")
        position += length
        if terminated:
            if frame[position] not in (0x00, 0xFF):
                raise SystemExit("fmsave: unexpected nation table layout")
            position += 1
        return text

    nations = []
    for _ in range(count):
        database_id = struct.unpack_from("<I", frame, position)[0]
        position += 4
        name = read_text(True)
        short_name = read_text(True)
        code = read_text(False)
        demonym = read_text(True)
        nations.append((database_id, name, short_name, code, demonym))
    if position != len(frame):
        raise SystemExit("fmsave: nation table has unexpected trailing data")
    return nations


def _a2g_nations(argv: list[str]) -> int:
    install = None
    output = None
    arguments = list(argv)
    while arguments:
        argument = arguments.pop(0)
        if argument == "--fm-install" and arguments:
            install = Path(arguments.pop(0))
        elif argument in ("-o", "--output") and arguments:
            output = Path(arguments.pop(0))
        else:
            raise SystemExit("usage: fmsave a2g-nations --fm-install <folder> -o <file.csv>")
    if install is None or output is None:
        raise SystemExit("usage: fmsave a2g-nations --fm-install <folder> -o <file.csv>")
    nations = _read_nations(_find_language_db(install))
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["database_id", "name", "short_name", "code", "demonym"])
        writer.writerows(nations)
    print(f"{len(nations)} nations written to {output}")
    return 0


def main() -> int:
    if len(sys.argv) > 1 and sys.argv[1] == "a2g-nations":
        return _a2g_nations(sys.argv[2:])
    from fmsave.cli import main as fmsave_main

    return fmsave_main()


if __name__ == "__main__":
    sys.exit(main())
