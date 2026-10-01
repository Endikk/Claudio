#!/usr/bin/env python3
"""Builds src/Claudio.App/Assets/Claudio.ico from Claudy's icon PNGs in claudy/Design/icon/.

Windows reads PNG images inside an .ico since Vista, so the PNGs go in as they are: no resampling,
no dependency. Run after Scripts/sync-claudy.ps1 whenever the icon changes.
"""
import os
import struct

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'claudy', 'Design', 'icon')
TARGET = os.path.join(ROOT, 'src', 'Claudio.App', 'Assets', 'Claudio.ico')
SIZES = (16, 32, 64, 128, 256)


def main():
    images = []
    for size in SIZES:
        with open(os.path.join(SOURCE, f'icon-{size}.png'), 'rb') as handle:
            images.append((size, handle.read()))

    header = struct.pack('<HHH', 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries, payload = b'', b''
    for size, data in images:
        side = 0 if size >= 256 else size  # 0 means 256 in an icon directory
        entries += struct.pack('<BBBBHHII', side, side, 0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)

    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    with open(TARGET, 'wb') as handle:
        handle.write(header + entries + payload)
    print(f'wrote {os.path.relpath(TARGET, ROOT)} ({", ".join(str(size) for size in SIZES)} px)')


if __name__ == '__main__':
    main()
