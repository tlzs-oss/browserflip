# Icon generator

Reproducibly generates everything in `assets/icon/` (SVGs, PNGs, ICO, preview sheet).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/icon/generate.ps1            # writes assets/icon/
powershell -ExecutionPolicy Bypass -File scripts/icon/generate.ps1 -OutputDirectory <dir>
```

Requirements: Windows, .NET 10 SDK (uses WPF, which ships with the SDK on Windows). No NuGet
packages. If `dotnet` is not on `PATH`, the per-user install in `%LOCALAPPDATA%\Microsoft\dotnet`
is used. Build output (`IconGen/bin`, `IconGen/obj`) is ignored by the root `.gitignore`.
The project is intentionally not part of `BrowserFlip.slnx`.

## How SVG and raster stay in sync

There is exactly **one** definition of the artwork: `IconGen/Design.cs`. It describes the icon as a
short list of primitives on a 256x256 canvas (rounded rect, circle/ellipse, and SVG path data using
only `M/L/H/V/A/Z`) with solid or `userSpaceOnUse` linear-gradient paints. From that list:

- `SvgWriter.cs` emits `browserflip.svg`, `browserflip-small.svg` and `browserflip-16.svg`;
- `Renderer.cs` draws the same primitives with WPF (`DrawingVisual` + `RenderTargetBitmap`;
  path strings go through `Geometry.Parse`, which accepts the same syntax) for every PNG/ICO size.

Never edit the SVG files by hand; change `Design.cs` and regenerate, so the SVG and the bitmaps can
never drift apart.

Which artwork is used for which pixel size:

| Size | Artwork |
| --- | --- |
| 16 | `Design.Tiny()` (pixel-fitted: 1 px = 16 units, lines on pixel centres) |
| 20, 24, 32 | `Design.Small()` |
| 40 and up | `Design.Full()` |

## ICO layout

`IcoWriter.cs` writes `ICONDIR` + one `ICONDIRENTRY` per image. The 256 px entry is a PNG payload;
16-64 px entries are 32-bit `BITMAPINFOHEADER` DIBs (bottom-up BGRA plus a 1-bit AND mask) for the
widest compatibility. After writing, the tool reads the header back and prints every entry.

## Files

| File | Purpose |
| --- | --- |
| `generate.ps1` | Entry point; locates `dotnet` and runs the generator |
| `IconGen/Design.cs` | The artwork and palette (single source of truth) |
| `IconGen/Model.cs` | Primitive/paint model and geometry helpers (arcs, arrowheads, chords) |
| `IconGen/SvgWriter.cs` | SVG emitter |
| `IconGen/Renderer.cs` | WPF rasterizer and PNG encoder |
| `IconGen/IcoWriter.cs` | ICO writer and header inspector |
| `IconGen/Preview.cs` | Preview sheet (light/dark backgrounds, pixel zooms) |
| `IconGen/Program.cs` | Orchestration and output file list |
