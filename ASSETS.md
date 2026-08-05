# Assets setup (required)

This mod renders the **NFSU2HUD 3.0** Assetto Corsa art pack. Those PNGs are **not** shipped in git or Releases (copyright).

## 1. Get NFSU2HUD 3.0

Download **NFSU2HUD 3.0** from RaceDepartment (search “NFSU2HUD”) or use the copy you already have for Assetto Corsa.

Inside the mod you need this folder:

```
NFSU2HUD 3.0\apps\python\NFSU2HUD\img\
```

It contains `background\`, `rev\`, `boost_needle\`, `speed\`, `gears_orange\`, etc.

## 2. Install next to the exe

Copy (or junction) that `img` folder to:

```
<wherever Nfsu2ForzaHud.exe lives>\Assets\AcHud\
```

Example after a Release unzip:

```
ForzaHorizon6-NFSU2-Speedometer\
  Nfsu2ForzaHud.exe
  app.ico
  Assets\
    AcHud\          ← contents of NFSU2HUD img\ go HERE
      background\
      rev\
      boost_needle\
      ...
```

### PowerShell junction (no duplicate disk use)

```powershell
$img = "C:\path\to\NFSU2HUD 3.0\apps\python\NFSU2HUD\img"
$dst = "C:\path\to\publish\Assets\AcHud"
New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
cmd /c mklink /J "$dst" "$img"
```

## 3. Verify

Run the exe. If assets are missing you’ll see:

`NFSU2HUD 3.0 assets not found`

Fix the `Assets\AcHud` path and restart.

## Optional search paths

The app also looks for:

- `Assets\AcHud` next to the exe
- `NFSU2HUD 3.0\apps\python\NFSU2HUD\img` under common Desktop / Air Gestures folders

Putting `Assets\AcHud` next to the exe is the supported install method.
