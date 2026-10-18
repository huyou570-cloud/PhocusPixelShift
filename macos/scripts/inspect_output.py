#!/usr/bin/env python3
"""Read first-IFD TIFF metadata without decoding or claiming color accuracy."""
import argparse
import json
import struct
from pathlib import Path


def inspect_tiff(path):
    path = Path(path)
    with path.open('rb') as stream:
        order = stream.read(2)
        if order not in (b'II', b'MM'):
            raise ValueError('Not a TIFF byte-order header')
        endian = '<' if order == b'II' else '>'

        def read(fmt):
            size = struct.calcsize(endian + fmt)
            data = stream.read(size)
            if len(data) != size:
                raise ValueError('Truncated TIFF metadata')
            return struct.unpack(endian + fmt, data)

        magic, = read('H')
        if magic == 42:
            offset, = read('I')
            count_fmt, entry_fmt, inline = 'H', 'HHI4s', 4
        elif magic == 43:
            width, reserved, offset = read('HHQ')
            if width != 8 or reserved:
                raise ValueError('Unsupported BigTIFF header')
            count_fmt, entry_fmt, inline = 'Q', 'HHQ8s', 8
        else:
            raise ValueError('Not a TIFF/BigTIFF header')
        size = path.stat().st_size
        if offset < 8 or offset >= size:
            raise ValueError('Invalid first IFD offset')
        stream.seek(offset)
        count, = read(count_fmt)
        if count > 4096:
            raise ValueError('Unreasonable TIFF tag count')
        values = {}
        formats = {1: 'B', 3: 'H', 4: 'I', 16: 'Q'}
        wanted = {256, 257, 258, 262, 277, 339}
        for _ in range(count):
            tag, kind, length, raw = read(entry_fmt)
            if tag not in wanted or kind not in formats:
                continue
            if not 1 <= length <= 128:
                raise ValueError('Invalid TIFF metadata value count')
            fmt = formats[kind] * length
            length_bytes = struct.calcsize(endian + fmt)
            if length_bytes <= inline:
                data = raw[:length_bytes]
            else:
                value_offset, = struct.unpack(endian + ('I' if inline == 4 else 'Q'), raw)
                if value_offset + length_bytes > size:
                    raise ValueError('TIFF value outside file')
                here = stream.tell()
                stream.seek(value_offset)
                data = stream.read(length_bytes)
                stream.seek(here)
            values[tag] = list(struct.unpack(endian + fmt, data))
        width = values.get(256, [0])[0]
        height = values.get(257, [0])[0]
        if width <= 0 or height <= 0:
            raise ValueError('Missing TIFF dimensions')
        return {'format': 'TIFF' if magic == 42 else 'BigTIFF', 'width': width,
                'height': height, 'bitsPerSample': values.get(258, [1]),
                'channels': values.get(277, [1])[0],
                'sampleFormat': values.get(339, [1]),
                'photometric': values.get(262), 'bytes': size,
                'inspectionScope': 'first IFD metadata only; pixel integrity, HNCS and color accuracy not verified'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('file', type=Path)
    args = parser.parse_args()
    print(json.dumps(inspect_tiff(args.file), indent=2))
