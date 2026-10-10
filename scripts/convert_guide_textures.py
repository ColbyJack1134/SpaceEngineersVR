#!/usr/bin/env python3
"""Convert guide PNGs to the game's DDS texture format (requires Pillow 12+)."""
from pathlib import Path
from PIL import Image

folder = Path(__file__).resolve().parents[1] / 'SpaceEngineersVR/Assets/Guide'
for source in sorted(folder.glob('*.png')):
    with Image.open(source) as image:
        image = image.convert('RGBA')
        # BC3 uses 4x4 blocks; pad only the bottom edge without scaling the artwork.
        height = (image.height + 3) // 4 * 4
        padded = Image.new('RGBA', (image.width, height), (0, 0, 0, 0))
        padded.paste(image, (0, 0))
        padded.save(source.with_suffix('.dds'), pixel_format='DXT5')
        print(source.stem)
