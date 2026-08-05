"""Crop/recenter NFSU2 race HUD masks so all layers share a common pivot."""
from __future__ import annotations

from pathlib import Path
import json
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "RaceReady"
OUT.mkdir(exist_ok=True)


def rgba(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.uint8)


def alpha_or_luma(arr: np.ndarray) -> np.ndarray:
    rgb = arr[..., :3].astype(np.float32)
    a = arr[..., 3].astype(np.float32)
    luma = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    # Prefer alpha if it carries the mask; else luma.
    if a.max() > 10 and (a > 10).sum() > 50:
        # If RGB is mostly black, use alpha; if RGB has content, use max(luma,a)
        if luma.max() < 8:
            return a
        return np.maximum(luma, a)
    return luma


def content_bbox(mask: np.ndarray, thr: float = 12.0):
    ys, xs = np.where(mask > thr)
    if len(xs) == 0:
        return 0, 0, mask.shape[1], mask.shape[0]
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def estimate_center(mask: np.ndarray, thr: float = 40.0):
    """Estimate dial center from bright ring / ticks via radial symmetry heuristic."""
    ys, xs = np.where(mask > thr)
    if len(xs) < 20:
        x0, y0, x1, y1 = content_bbox(mask)
        return (x0 + x1) / 2.0, (y0 + y1) / 2.0
    # Use median of bright pixels as rough center, then refine with circle fit on outer pixels.
    cx, cy = float(np.median(xs)), float(np.median(ys))
    # Keep points in mid-outer band.
    dist = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    lo, hi = np.percentile(dist, [55, 95])
    sel = (dist >= lo) & (dist <= hi)
    if sel.sum() < 30:
        return cx, cy
    xs2, ys2 = xs[sel].astype(np.float64), ys[sel].astype(np.float64)
    # Algebraic circle fit
    A = np.column_stack([2 * xs2, 2 * ys2, np.ones_like(xs2)])
    b = xs2**2 + ys2**2
    try:
        sol, *_ = np.linalg.lstsq(A, b, rcond=None)
        return float(sol[0]), float(sol[1])
    except Exception:
        return cx, cy


def make_square(arr: np.ndarray, cx: float, cy: float, half: int, thr: float = 12.0) -> Image.Image:
    h, w = arr.shape[:2]
    canvas = np.zeros((half * 2, half * 2, 4), dtype=np.uint8)
    # Source rect
    x0 = int(round(cx - half))
    y0 = int(round(cy - half))
    for y in range(half * 2):
        sy = y0 + y
        if sy < 0 or sy >= h:
            continue
        for x in range(half * 2):
            sx = x0 + x
            if sx < 0 or sx >= w:
                continue
            canvas[y, x] = arr[sy, sx]
    # Force near-black to transparent for clean compositing
    mask = alpha_or_luma(canvas)
    low = mask < thr
    canvas[low, 3] = 0
    return Image.fromarray(canvas, "RGBA")


def process_dial(name: str, path: Path, half: int | None = None):
    arr = rgba(path)
    mask = alpha_or_luma(arr)
    cx, cy = estimate_center(mask)
    x0, y0, x1, y1 = content_bbox(mask)
    # Radius from content extent
    rad = max(abs(cx - x0), abs(x1 - cx), abs(cy - y0), abs(y1 - cy))
    hlf = half or int(np.ceil(rad + 8))
    hlf = max(hlf, 32)
    img = make_square(arr, cx, cy, hlf)
    out = OUT / name
    img.save(out)
    print(f"{name}: center=({cx:.1f},{cy:.1f}) half={hlf} size={img.size}")
    return {"file": name, "center": [cx, cy], "half": hlf, "size": list(img.size)}


def process_needle(name: str, path: Path):
    arr = rgba(path)
    mask = alpha_or_luma(arr)
    x0, y0, x1, y1 = content_bbox(mask, thr=8)
    crop = arr[y0:y1, x0:x1].copy()
    # Transparent black
    m = alpha_or_luma(crop)
    crop[m < 8, 3] = 0
    img = Image.fromarray(crop, "RGBA")
    img.save(OUT / name)
    print(f"{name}: cropped {img.size} from {arr.shape[1]}x{arr.shape[0]}")
    return {"file": name, "size": list(img.size), "pivot": [0.5, 1.0]}  # tip up, pivot at bottom


def process_nos(name: str, path: Path):
    arr = rgba(path)
    mask = alpha_or_luma(arr)
    x0, y0, x1, y1 = content_bbox(mask, thr=8)
    pad = 4
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
    x1, y1 = min(arr.shape[1], x1 + pad), min(arr.shape[0], y1 + pad)
    crop = arr[y0:y1, x0:x1].copy()
    m = alpha_or_luma(crop)
    crop[m < 8, 3] = 0
    img = Image.fromarray(crop, "RGBA")
    img.save(OUT / name)
    print(f"{name}: cropped {img.size}")
    return {"file": name, "size": list(img.size)}


def main():
    meta = {}
    meta["tach_fill"] = process_dial("tach_fill.png", ROOT / "Race" / "tach_fill.png")
    meta["rpm_lines"] = process_dial("rpm_lines.png", ROOT / "Race" / "rpm_lines.png", half=meta["tach_fill"]["half"])
    # Re-export tach_fill with same half as lines for perfect overlay
    half = max(meta["tach_fill"]["half"], meta["rpm_lines"]["half"])
    meta["tach_fill"] = process_dial("tach_fill.png", ROOT / "Race" / "tach_fill.png", half=half)
    meta["rpm_lines"] = process_dial("rpm_lines.png", ROOT / "Race" / "rpm_lines.png", half=half)
    meta["redline"] = process_dial("redline.png", ROOT / "Race" / "redline.png", half=half)

    meta["turbo_fill"] = process_dial("turbo_fill.png", ROOT / "Race" / "turbo_fill.png")
    half_t = meta["turbo_fill"]["half"]
    meta["turbo_lines"] = process_dial("turbo_lines.png", ROOT / "Race" / "turbo_lines.png", half=half_t)
    half_t = max(meta["turbo_fill"]["half"], meta["turbo_lines"]["half"])
    meta["turbo_fill"] = process_dial("turbo_fill.png", ROOT / "Race" / "turbo_fill.png", half=half_t)
    meta["turbo_lines"] = process_dial("turbo_lines.png", ROOT / "Race" / "turbo_lines.png", half=half_t)

    meta["rpm_needle"] = process_needle("rpm_needle.png", ROOT / "Race" / "rpm_needle.png")
    meta["turbo_needle"] = process_needle("turbo_needle.png", ROOT / "Race" / "turbo_needle.png")
    meta["nos"] = process_nos("nos.png", ROOT / "Race" / "nos.png")
    meta["nos_backing"] = process_nos("nos_backing.png", ROOT / "Race" / "nos_backing.png")
    meta["nos_alpha"] = process_nos("nos_alpha.png", ROOT / "Race" / "nos_alpha.png")

    # Layout derived from ready art sizes (pixel-perfect base at 1:1 texture pixels, then scale)
    tach = half * 2
    turbo = half_t * 2
    nos_w, nos_h = meta["nos"]["size"]

    # Composition matching NFSU2: turbo nestled bottom-left of tach; NOS on top-right rim
    canvas_w = int(tach * 1.55)
    canvas_h = int(tach * 1.15)
    tach_x = int(canvas_w - tach - 20)
    tach_y = int(20)
    turbo_x = int(tach_x - turbo * 0.35)
    turbo_y = int(tach_y + tach - turbo * 0.72)
    nos_x = int(tach_x + tach * 0.55)
    nos_y = int(tach_y - 10)

    layout = {
        "canvasWidth": canvas_w,
        "canvasHeight": canvas_h,
        "anchor": "bottom-right",
        "marginX": 28,
        "marginY": 22,
        "scale": 0.85,
        "tintBlue": "#1E5BB8",
        "tintCyan": "#3EC4FF",
        "tintRed": "#C41E1E",
        "gearColor": "#FF7A18",
        "speedColor": "#FFFFFF",
        "unitColor": "#E8EEF7",
        "tach": {
            "x": tach_x,
            "y": tach_y,
            "width": tach,
            "height": tach,
            "needlePivotX": 0.5,
            "needlePivotY": 0.5,
            "angleStartDeg": 180,
            "angleEndDeg": 0,
            "needleWidth": max(18, meta["rpm_needle"]["size"][0]),
            "needleHeight": int(tach * 0.42),
            "faceMaxRpm": 8500
        },
        "turbo": {
            "x": turbo_x,
            "y": turbo_y,
            "width": turbo,
            "height": turbo,
            "needlePivotX": 0.5,
            "needlePivotY": 0.5,
            "angleStartDeg": 210,
            "angleEndDeg": -30,
            "needleWidth": max(12, meta["turbo_needle"]["size"][0]),
            "needleHeight": int(turbo * 0.40)
        },
        "nos": {
            "x": nos_x,
            "y": nos_y,
            "width": int(nos_w * (tach / 520)),
            "height": int(nos_h * (tach / 520))
        },
        "gear": {
            "x": tach_x + tach * 0.58,
            "y": tach_y + tach * 0.30,
            "fontSize": int(tach * 0.18)
        },
        "speed": {
            "x": tach_x + tach * 0.56,
            "y": tach_y + tach * 0.50,
            "fontSize": int(tach * 0.16)
        },
        "unit": {
            "x": tach_x + tach * 0.58,
            "y": tach_y + tach * 0.66,
            "fontSize": int(tach * 0.055)
        }
    }

    (OUT / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    layout_path = ROOT.parent / "Hud" / "race_layout.json"
    layout_path.write_text(json.dumps(layout, indent=2), encoding="utf-8")
    print("Wrote", layout_path)
    print("Ready art in", OUT)


if __name__ == "__main__":
    main()
