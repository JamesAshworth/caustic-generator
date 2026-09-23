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
5. Write the heights onto the mesh, solidify it (flat bottom + skirt), and save as binary STL.

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
dotnet run --project src/CausticGenerator.Cli -c Release -- <image> [output-directory] [options]
```

Writes `original_image.stl` plus per-iteration `loss_itN.png` diagnostics (positive loss blue, negative
red) to the output directory (default `./output`). Run with `--help` for the same table below.

| Flag | Default | Effect |
|------|---------|--------|
| `--artifact-size <metres>` | `0.1` | Width of the printed lens |
| `--focal-length <metres>` | `0.2` | Distance from lens to projection surface |
| `--iterations <n>` | `4` | Outer march iterations |
| `--loss-divisor <n\|pixels>` | `262144` | Divisor that zero-centres the loss field. `pixels` uses the true pixel count |
| `--height-scale <x>` | `1` | Multiplier applied to solved heights |
| `--height-offset <x>` | `10` | Constant added to solved heights |
| `--solidify-offset <x>` | `100` | Depth of the flat bottom below the lens |
| `--output <dir>` | `./output` | Same as the positional output directory, and wins over it |
| `--no-loss-images` | off | Skip the loss PNG diagnostics |
| `--save-obj` | off | Also write OBJ alongside the STL |
| `-h`, `--help` | | Show usage |

STL is the default output: it is what slicers want, and binary STL keeps the file compact. OBJ preserves
vertex sharing and the grid dimensions, so it can be loaded back into a `Mesh` with `ObjWriter.Load`,
which STL cannot.

Everything on `CausticsOptions` has a flag, and a test fails if a new option is added without one.
Options are order-independent and may precede the image.

```bash
# Bigger, longer-throw lens, three iterations, true-pixel-count loss normalisation
dotnet run --project src/CausticGenerator.Cli -c Release -- cat.jpg ./out \
    --artifact-size 0.15 --focal-length 0.3 --iterations 3 --loss-divisor pixels
```

Or publish once and invoke the binary:

```bash
dotnet publish src/CausticGenerator.Cli -c Release -o ./dist
./dist/caustic-generator cat.jpg ./out --save-obj
```

## Deviations from the Julia original

- **Loss normalisation divisor.** Upstream divides the loss sum by a hardcoded `512 * 512` regardless of
  image size. That is reproduced by default so output matches; set
  `CausticsOptions.LossNormalisationDivisor` to use the true pixel count instead.
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

72 tests covering triangle geometry, mesh construction and solidification, gradient and pixel-area
computation, relaxation convergence (including that a non-zero-mean source has no solution), the
non-inverting march guarantee, STL facet layout and normals, OBJ round-tripping, CLI parsing of every
option, and the end-to-end pipeline.

Targets .NET 10.
