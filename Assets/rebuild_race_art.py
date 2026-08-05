"""Rebuild race HUD layers: full circular backings + shared exact center."""
from __future__ import annotations

import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "Race"
OUT = ROOT / "RaceReady"
OUT.mkdir(exist_ok=True)

SIZE = 640  # square atlas
CENTER = SIZE // 2  # 320
# Dial radius in atlas pixels (leave margin for NOS tab)
RADIUS = 250


def load_rgba(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.uint8)


def mask_strength(arr: np.ndarray) -> np.ndarray:
    rgb = arr[..., :3].astype(np.float32).max(axis=2)
    a = arr[..., 3].astype(np.float32)
    if a.max() > 10 and rgb.max() < 8:
        return a
    return np.maximum(rgb, a)


def fit_center(mask: np.ndarray, thr: float = 40.0):
    ys, xs = np.where(mask > thr)
    if len(xs) < 30:
        return mask.shape[1] / 2.0, mask.shape[0] / 2.0
    cx, cy = float(np.median(xs)), float(np.median(ys))
    dist = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    lo, hi = np.percentile(dist, [55, 94])
    sel = (dist >= lo) & (dist <= hi)
    if sel.sum() < 40:
        return cx, cy
    xs2, ys2 = xs[sel].astype(np.float64), ys[sel].astype(np.float64)
    A = np.column_stack([2 * xs2, 2 * ys2, np.ones_like(xs2)])
    b = xs2**2 + ys2**2
    sol, *_ = np.linalg.lstsq(A, b, rcond=None)
    return float(sol[0]), float(sol[1])


def paste_centered(src: np.ndarray, src_cx: float, src_cy: float, scale: float) -> np.ndarray:
    """Resample src so (src_cx,src_cy) lands on CENTER and distances scale."""
    h, w = src.shape[:2]
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    # map output pixel -> source pixel
    sx = src_cx + (xx - CENTER) / scale
    sy = src_cy + (yy - CENTER) / scale
    out = np.zeros((SIZE, SIZE, 4), dtype=np.uint8)
    # nearest sample with bounds
    sxi = np.rint(sx).astype(np.int32)
    syi = np.rint(sy).astype(np.int32)
    valid = (sxi >= 0) & (sxi < w) & (syi >= 0) & (syi < h)
    out[valid] = src[syi[valid], sxi[valid]]
    return out


def to_white_alpha(arr: np.ndarray, thr: float = 24.0) -> np.ndarray:
    m = mask_strength(arr)
    out = np.zeros_like(arr)
    sel = m > thr
    out[sel, 0] = out[sel, 1] = out[sel, 2] = 255
    out[sel, 3] = np.clip(m[sel], 0, 255).astype(np.uint8)
    return out


def make_disc(radius: int, color=(255, 255, 255, 220), edge_soft=3) -> np.ndarray:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = radius
    # soft edge via a few circles
    for i in range(edge_soft, 0, -1):
        a = int(color[3] * (i / edge_soft) * 0.35)
        d.ellipse((CENTER - r - i, CENTER - r - i, CENTER + r + i, CENTER + r + i), fill=(color[0], color[1], color[2], a))
    d.ellipse((CENTER - r, CENTER - r, CENTER + r, CENTER + r), fill=color)
    return np.asarray(img, dtype=np.uint8)


def crop_needle(path: Path, out_name: str):
    arr = load_rgba(path)
    m = mask_strength(arr)
    ys, xs = np.where(m > 8)
    x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    crop = arr[y0:y1, x0:x1].copy()
    mm = mask_strength(crop)
    crop[mm < 8, 3] = 0
    # white-alpha
    out = to_white_alpha(crop, 8)
    Image.fromarray(out).save(OUT / out_name)
    print(out_name, out.shape[1], out.shape[0])


def main():
    # Authoritative center from RPM lines
    lines_src = load_rgba(SRC / "rpm_lines.png")
    lines_m = mask_strength(lines_src)
    lcx, lcy = fit_center(lines_m)
    # scale so content radius maps to RADIUS
    ys, xs = np.where(lines_m > 40)
    rad = np.percentile(np.sqrt((xs - lcx) ** 2 + (ys - lcy) ** 2), 98)
    scale = RADIUS / max(rad, 1)
    print(f"rpm_lines center=({lcx:.1f},{lcy:.1f}) rad={rad:.1f} scale={scale:.3f}")

    lines = to_white_alpha(paste_centered(lines_src, lcx, lcy, scale))
    Image.fromarray(lines).save(OUT / "rpm_lines.png")

    # Tach fill — use SAME center/scale as lines by estimating its center then aligning
    fill_src = load_rgba(SRC / "tach_fill.png")
    fcx, fcy = fit_center(mask_strength(fill_src), thr=20)
    # Prefer aligning using scale from lines; re-estimate fill radius independently but force center map to CENTER
    fill = to_white_alpha(paste_centered(fill_src, fcx, fcy, scale), thr=12)
    Image.fromarray(fill).save(OUT / "tach_fill.png")
    print(f"tach_fill src_center=({fcx:.1f},{fcy:.1f})")

    # NOS — fit center from original, paste with scale so it matches tach radius
    nos_src = load_rgba(SRC / "nos.png")
    ncx, ncy = fit_center(mask_strength(nos_src), thr=80)
    # NOS authored for same dial — use lines scale but NOS center
    nos = to_white_alpha(paste_centered(nos_src, ncx, ncy, scale), thr=40)
    Image.fromarray(nos).save(OUT / "nos.png")
    print(f"nos src_center=({ncx:.1f},{ncy:.1f})")

    nos_b_src = load_rgba(SRC / "nos_backing.png")
    # backing often alpha-only
    nb = paste_centered(nos_b_src, ncx, ncy, scale)
    # if rgb empty use alpha
    if nb[..., :3].max() < 8:
        a = nb[..., 3]
        out = np.zeros_like(nb)
        sel = a > 20
        out[sel, 0] = out[sel, 1] = out[sel, 2] = 255
        out[sel, 3] = a[sel]
        nb = out
    else:
        nb = to_white_alpha(nb, 20)
    Image.fromarray(nb).save(OUT / "nos_backing.png")

    # Circular backings (what the game composites under the masks)
    disc = make_disc(RADIUS - 6, color=(255, 255, 255, 200))
    Image.fromarray(disc).save(OUT / "tach_disc.png")
    ring = make_disc(RADIUS + 4, color=(255, 255, 255, 40))
    # hollow-ish outer by subtracting inner
    inner = make_disc(RADIUS - 18, color=(255, 255, 255, 255))
    ring_a = ring.astype(np.int16)
    ring_a[inner[..., 3] > 0, 3] = 0
    ring = np.clip(ring_a, 0, 255).astype(np.uint8)
    Image.fromarray(ring).save(OUT / "tach_ring.png")

    # Turbo
    t_lines_src = load_rgba(SRC / "turbo_lines.png")
    tcx, tcy = fit_center(mask_strength(t_lines_src))
    tys, txs = np.where(mask_strength(t_lines_src) > 40)
    trad = np.percentile(np.sqrt((txs - tcx) ** 2 + (tys - tcy) ** 2), 98)
    TURBO_SIZE = 320
    TURBO_CENTER = TURBO_SIZE // 2
    TURBO_RADIUS = 130
    tscale = TURBO_RADIUS / max(trad, 1)

    def paste_turbo(src, scx, scy):
        yy, xx = np.mgrid[0:TURBO_SIZE, 0:TURBO_SIZE]
        sx = scx + (xx - TURBO_CENTER) / tscale
        sy = scy + (yy - TURBO_CENTER) / tscale
        out = np.zeros((TURBO_SIZE, TURBO_SIZE, 4), dtype=np.uint8)
        sxi = np.rint(sx).astype(np.int32)
        syi = np.rint(sy).astype(np.int32)
        valid = (sxi >= 0) & (sxi < src.shape[1]) & (syi >= 0) & (syi < src.shape[0])
        out[valid] = src[syi[valid], sxi[valid]]
        return out

    Image.fromarray(to_white_alpha(paste_turbo(t_lines_src, tcx, tcy))).save(OUT / "turbo_lines.png")
    t_fill_src = load_rgba(SRC / "turbo_fill.png")
    tfcx, tfcy = fit_center(mask_strength(t_fill_src), thr=15)
    Image.fromarray(to_white_alpha(paste_turbo(t_fill_src, tfcx, tfcy), 10)).save(OUT / "turbo_fill.png")

    tdisc = Image.new("RGBA", (TURBO_SIZE, TURBO_SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(tdisc)
    r = TURBO_RADIUS - 4
    d.ellipse((TURBO_CENTER - r, TURBO_CENTER - r, TURBO_CENTER + r, TURBO_CENTER + r), fill=(255, 255, 255, 200))
    tdisc.save(OUT / "turbo_disc.png")

    crop_needle(SRC / "rpm_needle.png", "rpm_needle.png")
    crop_needle(SRC / "turbo_needle.png", "turbo_needle.png")

    # Redline arc — same center/scale as tach face
    rl_src = load_rgba(SRC / "redline.png")
    Image.fromarray(to_white_alpha(paste_centered(rl_src, lcx, lcy, scale), 20)).save(OUT / "redline.png")
    print("redline.png")

    # Preview
    def tint(arr, rgb, mul=1.0):
        out = np.zeros_like(arr)
        a = arr[..., 3].astype(np.float32) / 255.0 * mul
        out[..., 0] = np.clip(rgb[2] * a, 0, 255)  # B in RGB sense for PIL is R,G,B
        out[..., 1] = np.clip(rgb[1] * a, 0, 255)
        out[..., 2] = np.clip(rgb[0] * a, 0, 255)
        out[..., 3] = np.clip(arr[..., 3] * mul, 0, 255)
        return out.astype(np.uint8)

    # Fix tint channels: PIL RGBA is R,G,B,A
    def tint_rgb(arr, rgb, mul=1.0):
        out = np.zeros_like(arr)
        a = arr[..., 3].astype(np.float32) / 255.0 * mul
        out[..., 0] = np.clip(rgb[0] * a, 0, 255)
        out[..., 1] = np.clip(rgb[1] * a, 0, 255)
        out[..., 2] = np.clip(rgb[2] * a, 0, 255)
        out[..., 3] = np.clip(arr[..., 3] * mul, 0, 255)
        return out.astype(np.uint8)

    layers = [
        tint_rgb(disc, (20, 40, 80), 0.85),
        tint_rgb(fill, (30, 90, 200), 0.9),
        tint_rgb(lines, (255, 255, 255), 1.0),
        tint_rgb(nos, (62, 196, 255), 1.0),
    ]
    comp = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 255))
    for layer in layers:
        comp = Image.alpha_composite(comp, Image.fromarray(layer))
    # red cross at center
    draw = ImageDraw.Draw(comp)
    draw.ellipse((CENTER - 3, CENTER - 3, CENTER + 3, CENTER + 3), outline=(255, 0, 0, 255))
    comp.save(OUT / "preview_final.png")

    meta = {
        "size": SIZE,
        "center": [CENTER, CENTER],
        "radius": RADIUS,
        "turbo_size": TURBO_SIZE,
        "turbo_center": [TURBO_CENTER, TURBO_CENTER],
        "turbo_radius": TURBO_RADIUS,
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")

    # Layout: turbo nested tight into tach bottom-left (NFSU2 reference)
    tach = 420
    turbo = 200
    pad = 24
    tach_x = 88
    tach_y = pad
    turbo_x = 72
    turbo_y = tach_y + tach - turbo * 0.81
    canvas_w = tach_x + tach + pad
    canvas_h = max(tach_y + tach, turbo_y + turbo) + pad

    layout = {
        "canvasWidth": int(canvas_w),
        "canvasHeight": int(canvas_h),
        "anchor": "bottom-right",
        "marginX": 56,
        "marginY": 28,
        "scale": 0.52,
        "tintBlue": "#1E5BB8",
        "tintCyan": "#3EC4FF",
        "tintRed": "#C41E1E",
        "gearColor": "#FF7A18",
        "speedColor": "#FFFFFF",
        "unitColor": "#E8EEF7",
        "tach": {
            "x": int(tach_x),
            "y": int(tach_y),
            "width": tach,
            "height": tach,
            "needlePivotX": 0.5,
            "needlePivotY": 0.5,
            "angleStartDeg": 0,
            "angleEndDeg": 240,
            "needleWidth": 17,
            "needleHeight": 188,
            "faceMaxRpm": 20000
        },
        "turbo": {
            "x": int(turbo_x),
            "y": int(turbo_y),
            "width": turbo,
            "height": turbo,
            "needlePivotX": 0.5,
            "needlePivotY": 0.5,
            "angleStartDeg": 30,
            "angleEndDeg": 240,
            "needleWidth": 12,
            "needleHeight": 90
        },
        "nos": {
            "x": int(tach_x),
            "y": int(tach_y),
            "width": tach,
            "height": tach
        },
        "gear": {
            "x": int(tach_x + tach * 0.57),
            "y": int(tach_y + tach * 0.33),
            "fontSize": 52
        },
        "speed": {
            "x": int(tach_x + tach * 0.54),
            "y": int(tach_y + tach * 0.49),
            "fontSize": 46
        },
        "unit": {
            "x": int(tach_x + tach * 0.57),
            "y": int(tach_y + tach * 0.61),
            "fontSize": 15
        }
    }
    layout_path = ROOT.parent / "Hud" / "race_layout.json"
    layout_path.write_text(json.dumps(layout, indent=2), encoding="utf-8")
    print("Wrote", layout_path)
    print("Preview", OUT / "preview_final.png")


if __name__ == "__main__":
    main()
