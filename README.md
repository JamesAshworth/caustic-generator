# caustic-generator

C# port of [MattFerraro/causticsEngineering](https://github.com/MattFerraro/causticsEngineering) — given an
image, it solves for the shape of a transparent lens that, when lit from behind, focuses light into that
image as a caustic pattern. Output is a watertight solid mesh in Wavefront OBJ, ready for slicing.

The original is Julia; this is a direct transliteration of the same algorithm. See
`THIRD-PARTY-NOTICES` for the upstream MIT licence and copyright.

## How it works

1. Load the image as a greyscale field and scale it so its total brightness equals the total area of an
   undistorted mesh — otherwise the solver chases a target it cannot reach.
2. Build a square mesh one node wider and taller than the image, so every pixel quad can be closed.
3. Repeat (4 times by default):
   - Measure the area of each mesh cell. Cell area is the light each cell delivers.
   - `loss = area - target`, shifted to sum to zero.
   - Solve `laplacian(phi) = loss` by successive over-relaxation with Neumann boundaries.
   - March every node down `grad(phi)`. The step is half the smallest positive time at which any
     triangle would collapse, so no triangle can invert.
4. Turn the resulting node displacements into refracted ray angles, apply Snell's law at n = 1.49 to get
   the required surface normals, and solve a second Poisson problem for the height field whose gradient
   matches them.
5. Write the heights onto the mesh, converting from pixels to millimetres and shifting the surface so
   its lowest point is z = 0, solidify it (flat back face + skirt), and save as binary STL.

## Layout

| Path | Contents |
|------|----------|
| `src/CausticsEngineering/Geometry/` | `Point3D`, `Triangle`, `Mesh`, `MeshBuilder` (square mesh, solidify) |
| `src/CausticsEngineering/Solver/` | `ScalarField` (gradient, pixel area, relaxation), `MeshMarcher`, `SurfaceSolver` |
| `src/CausticsEngineering/Io/` | `StlWriter` (default output), `ObjWriter`, `ImageIo` (greyscale load, loss visualisation) |
| `src/CausticsEngineering/CausticsEngine.cs` | Top-level pipeline and `CausticsOptions` |
| `src/CausticGenerator.Cli/` | Console entry point |
| `tests/CausticsEngineering.Tests/` | NUnit tests |

## Usage

```bash
dotnet run --project src/CausticGenerator.Cli -c Release -- <image> [options]
```

Writes `original_image.stl` plus per-iteration `loss_itN.png` diagnostics (positive loss blue, negative
red) to the output directory, which defaults to the working directory. Run with `--help` for the same
table below.

| Flag | Default | Effect |
|------|---------|--------|
| `--artifact-size <mm>` | `100` | Longest edge of the printed lens |
| `--focal-length <mm>` | `200` | Distance from lens to projection surface |
| `--iterations <n>` | `4` | Outer march iterations |
| `--resize <n\|none>` | `none` | Cap the image's longest edge at n before solving, preserving aspect, to cap solver cost |
| `--loss-divisor <n\|pixels>` | `pixels` | Divisor that zero-centres the loss field. Pass `262144` for parity with upstream |
| `--minimum-depth <mm>` | `10` | Material thickness at the thinnest point |
| `--output <dir>` | cwd | Where to write output |
| `--no-loss-images` | off | Skip the loss PNG diagnostics |
| `--save-obj` | off | Also write OBJ alongside the STL |
| `-h`, `--help` | | Show usage |

STL is the default output: it is what slicers want, and binary STL keeps the file compact. OBJ preserves
vertex sharing and the grid dimensions, so it can be loaded back into a `Mesh` with `ObjWriter.Load`,
which STL cannot.

Everything on `CausticsOptions` has a flag, and a test fails if a new option is added without one.
Options are order-independent and may precede the image.

### Units and scale

**All lengths are millimetres**, in and out. STL and OBJ are unitless and slicers read both as
millimetres, so the saved model needs no scaling on import.

**Aspect ratio is preserved throughout.** Both `--artifact-size` and `--resize` key off the image's
**longer** axis, so the scale is `artifactSize / max(imageWidth, imageHeight)` and nothing is pinned
to a particular image size or orientation. `--artifact-size` is therefore the longest edge of the
lens, and the other edge follows from the image's proportions — a 2:1 image at `--artifact-size 80`
gives an 80 x 40 mm lens, or 40 x 80 mm if it is portrait.

`--resize` is off by default, solving at the image's native size. Given a value it caps the longer
edge and scales the other to match, never upscaling a smaller image. Worth setting for a large
input: both Poisson solves cost O(pixels) per sweep over thousands of sweeps.

Depth is expressed as one number rather than two offsets. The solved surface is shifted so its
**lowest point sits at exactly z = 0**, and the flat back face is placed `--minimum-depth` below
that, so the flag is literally the material thickness at the thinnest point of the lens:

```
z =  0 + relief   <- lens surface, lowest point at exactly 0
z = -minimumDepth <- flat back face
```

The model's bounding box is therefore the artifact size on its longer axis, the aspect-scaled
length on the other, and `minimumDepth + relief` deep. XY starts at the origin, give or take a few
hundredths of a millimetre where marched edge nodes drift just outside the grid.

```bash
# Bigger, longer-throw lens, three iterations, thinner at its thinnest point
dotnet run --project src/CausticGenerator.Cli -c Release -- cat.jpg --output ./out \
    --artifact-size 150 --focal-length 300 --iterations 3 --minimum-depth 4
```

Or publish once and invoke the binary:

```bash
dotnet publish src/CausticGenerator.Cli -c Release -o ./dist
./dist/caustic-generator cat.jpg --output ./out --save-obj
```

## Deviations from the Julia original

- **Scale is derived, not hardcoded.** Upstream hardcodes `512` in two places: the loss-normalisation
  divisor, and the metres-per-unit factor applied when saving. Both are computed from the image here,
  off its longer axis, which is the whole reason output can be trusted at sizes and aspect ratios
  other than 512 x 512. Pass `--loss-divisor 262144` for bit-for-bit parity with upstream on other
  sizes.
- **Depth is one flag, not two offsets.** Upstream has a `heightOffset` added to solved heights and a
  separate `solidify` offset for the back face, both in pixel units, with the surface free to sit
  anywhere relative to z = 0. This zeroes the lowest point of the surface and takes a single
  `--minimum-depth` in millimetres. See "Units and scale".
- **Output is in millimetres.** Upstream writes metres, scaled by the hardcoded 512 factor.
- **No height-scale knob.** Upstream's `setHeights!` takes a `heightScale` multiplier, though it only
  ever passes 1. Scaling the solved relief scales every refraction angle with it, so `heightScale: k`
  produces a lens that focuses at roughly `f / k` while still claiming `f` — measurably so: doubling it
  matches halving `--focal-length` to within 0.5%. `--focal-length` does the same job consistently, so
  the multiplier is gone rather than kept as a trap.
- **Relaxation branches collapsed.** The original spells out nine cases (four corners, four edges,
  interior). This sums the neighbours that exist and divides by that count, which is arithmetically
  identical.
- **NaN during relaxation throws** rather than printing and returning a partial result.
- **Greyscale conversion** uses Rec. 601 luma weights (0.299/0.587/0.114) to match Julia's `Gray`.
- **Output format.** Upstream saves OBJ only. This writes binary STL by default, with OBJ available via
  `AlsoSaveObj`. STL has no vertex sharing, so each facet is emitted standalone with a computed normal.
- **Not ported:** the Julia `quiver` plotting helpers (`plotAsQuiver`, `plotVAsQuiver`) and the
  interactive test scratchpads, which were debugging aids for the original blog post.
- Vertex indices stay 1-based to match the OBJ face convention; array indices are 0-based.

## Tests

```bash
dotnet test
```

91 tests covering triangle geometry, mesh construction and solidification, gradient and pixel-area
computation, relaxation convergence (including that a non-zero-mean source has no solution), the
non-inverting march guarantee, STL facet layout and normals, OBJ round-tripping, CLI parsing of every
option, image loading and aspect-preserving resize, and the end-to-end pipeline. The depth and scale
guarantees are asserted against the bounds of the saved STL, so they are checked as a slicer would see
them rather than in memory, in both landscape and portrait.

Targets .NET 10.
