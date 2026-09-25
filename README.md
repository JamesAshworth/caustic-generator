# caustic-generator

C# port of [MattFerraro/causticsEngineering](https://github.com/MattFerraro/causticsEngineering) — given an
image, it solves for the shape of a transparent lens that, when lit from behind, focuses light into that
image as a caustic pattern. Output is a watertight solid: binary STL by default, ready for slicing, with
Wavefront OBJ and a STEP CAD solid also available. Pick formats with `--save-stl`, `--save-obj` and
`--save-step`.

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
4. Turn the resulting node displacements into refracted ray angles, apply Snell's law at the material's
   refractive index (`--refractive-index`, default 1.49 for cast acrylic) to get the required surface
   normals, and solve a second Poisson problem for the height field whose gradient matches them.
5. Write the heights onto the mesh, converting from pixels to millimetres and shifting the surface so
   its lowest point is z = 0, solidify it (flat back face + skirt), and save as binary STL.

## Layout

| Path | Contents |
|------|----------|
| `src/CausticsEngineering/Geometry/` | `Point3D`, `Vec3`, `Triangle`, `Mesh`, `MeshBuilder` (square mesh, solidify), `BSplineSurface` |
| `src/CausticsEngineering/Solver/` | `ScalarField` (gradient, pixel area, relaxation), `MeshMarcher`, `SurfaceSolver` |
| `src/CausticsEngineering/Io/` | `StlWriter` (default output), `ObjWriter`, `StepWriter`, `ImageIo` (greyscale load, loss visualisation) |
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
| `--refractive-index <n>` | `1.49` | Refractive index of the lens material, must be > 1 |
| `--iterations <n>` | `4` | Outer march iterations |
| `--resize <n\|none>` | `none` | Cap the image's longest edge at n before solving, preserving aspect, to cap solver cost |
| `--loss-divisor <n\|pixels>` | `pixels` | Divisor that zero-centres the loss field. Pass `262144` for parity with upstream |
| `--minimum-depth <mm>` | `10` | Material thickness at the thinnest point |
| `--output <dir>` | cwd | Where to write output |
| `--no-loss-images` | off | Skip the loss PNG diagnostics |
| `--save-stl` | on unless another is given | Write binary STL |
| `--save-obj` | off | Write Wavefront OBJ |
| `--save-step` | off | Write a STEP CAD solid |
| `-h`, `--help` | | Show usage |

### Mesh formats

The three format flags combine freely and are order-independent. **Give none and STL is written. Give
one or more and exactly those are written** — so `--save-step` on its own writes STEP and no STL, while
`--save-stl --save-step` writes both. Repeating a flag is harmless.

| Format | File | Why |
|--------|------|-----|
| STL | `original_image.stl` | The default. What slicers want, and binary STL keeps it compact |
| OBJ | `original_image.obj` | Keeps vertex sharing and the grid dimensions, so `ObjWriter.Load` can read it back into a `Mesh`, which STL cannot |
| STEP | `original_image.stp` | A CAD solid rather than a mesh — see below |

Through the library the same choice is one `Formats` property on `CausticsOptions`, a `[Flags]` enum
defaulting to `OutputFormats.Stl`. `OutputFormats.None` solves and returns the result without writing a
mesh, which is useful when only the loss diagnostics or the in-memory surface are wanted.

### STEP output

`--save-step` writes `original_image.stp`: the same solid as the STL, but as a boundary
representation rather than a mesh. The lens surface becomes a single bicubic B-spline patch, and the
skirt and back face become five planes, so the solid has **six faces** where the STL has half a million
facets. For an 85 mm lens off a 440 x 289 image that is 11 MB against the STL's 25 MB, and it opens as a
solid with editable faces instead of a facet soup a CAD kernel has to be talked into accepting.

Written as AP214 (`automotive_design`), the most widely read STEP flavour, with millimetre units
declared explicitly so the recipient's importer never has to infer the scale.

Three properties make the solid watertight by construction rather than within a tolerance:

- The fit **interpolates** the solved nodes rather than using them as control points. Feeding a node
  grid straight in as a control net would smooth the relief, and the relief is the caustic.
- Each skirt face is bounded by the **surface's own boundary curve** — the same control points and
  knots, not a copy fitted separately. A copy would only ever sew to a tolerance.
- The four boundary control rows are **pinned to the rectangle**. Marched edge nodes drift a few
  hundredths of a millimetre outside the grid, so they are snapped to the same rectangle
  `Solidify` gives the back face, which is what makes all four skirts exactly planar rather than
  doubly curved. The snap alone is not quite enough: a B-spline reproduces a constant exactly in
  theory but the collocation solve does so only to round-off, erratically and depending on the data,
  so the rows are pinned afterwards.

The cost is one control point per pixel — 127,890 for the example above. That is a large single patch,
and a CAD kernel will take a moment over it. Pair `--save-step` with `--resize` if the recipient's
tooling struggles.

STEP output has not been opened in a commercial CAD package as part of this work. What is verified, by
test, is that the file parses, has no dangling references, declares millimetres, carries exactly six
faces on one closed shell, uses every edge exactly once in each direction, and keeps every boundary
curve bit-exactly in its skirt plane.

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

# STEP only, for handing to CAD, with the solve capped to keep the patch manageable
dotnet run --project src/CausticGenerator.Cli -c Release -- cat.jpg --output ./out \
    --save-step --resize 300
```

Or publish once and invoke the binary:

```bash
dotnet publish src/CausticGenerator.Cli -c Release -o ./dist
./dist/caustic-generator cat.jpg --output ./out --save-stl --save-obj
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
- **Output format.** Upstream saves OBJ only. This writes binary STL by default and takes any
  combination of STL, OBJ and STEP. STL has no vertex sharing, so each facet is emitted standalone with
  a computed normal. STEP has no upstream counterpart at all: it is a B-rep solid built from a B-spline
  fit of the solved surface rather than any form of the triangle mesh.
- **Not ported:** the Julia `quiver` plotting helpers (`plotAsQuiver`, `plotVAsQuiver`) and the
  interactive test scratchpads, which were debugging aids for the original blog post.
- Vertex indices stay 1-based to match the OBJ face convention; array indices are 0-based.

## Tests

```bash
dotnet test
```

128 tests covering triangle geometry, mesh construction and solidification, gradient and pixel-area
computation, relaxation convergence (including that a non-zero-mean source has no solution), the
non-inverting march guarantee, STL facet layout and normals, OBJ round-tripping, B-spline interpolation
and its exact corner behaviour, STEP topology and planarity, CLI parsing of every option, image loading
and aspect-preserving resize, format selection writing exactly the files asked for, and the end-to-end
pipeline. The depth and scale guarantees are asserted
against the bounds of the saved STL and STEP, so they are checked as a slicer or a CAD importer would
see them rather than in memory, in both landscape and portrait.

Targets .NET 10.
