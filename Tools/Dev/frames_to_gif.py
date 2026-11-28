"""Собирает PNG-кадры (например, из ColorsAction.Unity.Editor.AnimationCapture) в зацикленную GIF.

    python Tools/Dev/frames_to_gif.py <папка с frame_*.png> <out.gif> [--fps 15] [--scale 1.0]

Нужен только Pillow.
"""
import argparse
import glob
import os

from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder")
    parser.add_argument("out")
    parser.add_argument("--fps", type=float, default=15)
    parser.add_argument("--scale", type=float, default=1.0)
    args = parser.parse_args()

    paths = sorted(glob.glob(os.path.join(args.folder, "frame_*.png")))
    if not paths:
        raise SystemExit(f"no frame_*.png in {args.folder}")
    frames = []
    for p in paths:
        im = Image.open(p).convert("RGB")
        if args.scale != 1.0:
            im = im.resize((int(im.width * args.scale), int(im.height * args.scale)), Image.LANCZOS)
        frames.append(im.quantize(colors=128, method=Image.MEDIANCUT, dither=Image.NONE))
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    frames[0].save(args.out, save_all=True, append_images=frames[1:], duration=int(1000 / args.fps), loop=0, optimize=True)
    print(f"{len(frames)} frames -> {args.out}")


if __name__ == "__main__":
    main()
