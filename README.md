# FoxholeModel

Headless CLI tools for inspecting and rendering Unreal Engine (UE4) assets – tuned for the Foxhole game data – using CUE4Parse and a lightly ported FModel renderer. Runs in a hidden OpenGL context and exports PNG screenshots from a JSON scene description.

- Static + skeletal meshes (including blueprint‑discovered components)
- Blueprint traversal: SCS/ICH templates, ChildActorComponents, default‑object properties (e.g., `SkelMesh`, `FlagMesh`)
- Component material overrides, MIC parent climb, vertex color overrides
- Socket attachments and transform offsets (sometimes)
- Scene filtering (path/tags) and material visibility (show/hide by token)
- Cross‑platform convenience: WSL→PowerShell wrapper for Windows Foxhole installs

This repository is GPL‑3.0 (see `LICENSE`). Do not commit proprietary game data or keys.

## Quick Start

Prerequisites
- Windows with Foxhole installed via Steam (for default pak path) and WSL (Ubuntu recommended)
- .NET 8 SDK (`dotnet --version` ≥ 8)
- PowerShell (Windows PowerShell or PowerShell 7)

Build
```bash
# from repo root (in WSL)
dotnet restore
dotnet build -c Release
```

Render (WSL wrapper; no extra `--` before app args)
```bash
# Example: render a JSON scene
scripts/render-windows.sh render \
  --game-version GAME_UE4_24 \
  --scene output/scene_bpatgunait2.json \
  --verbose
```
The wrapper calls `scripts/render.ps1` on Windows, which in turn calls `dotnet run -c Release -- …`.

Pak discovery
- If `--pak-dir` is not provided, the CLI searches common paths, preferring:
  - `C:\\Program Files (x86)\\Steam\\steamapps\\common\\Foxhole\\War\\Content\\Paks`
  - WSL mirrors under `/mnt/c/...`

Environment hints
- `CUE4PARSE_SKIP_NATIVE=1` (we set this in the wrapper) to skip native extraction helpers
- `HEADLESS_ENABLE_PP=1` opt‑in to experimental post‑processing (see below)

## Scene JSON (overview)

Minimal example
```jsonc
{
  "assets": [
    { "id": "root", "path": "War/Content/Meshes/Structures/FortTrenches/FortT2Floor01.FortT2Floor01" }
  ],
  "camera": { "pitch": 55, "yaw": 45, "angles": 1, "orbit": 0, "width": 1600, "height": 900, "transparent": false },
  "render": { "output": "output/renders" }
}
```

Blueprint example with attachments, sockets, filters and material visibility (doesnt work every time)
```jsonc
{
  "assets": [
    {
      "id": "root",
      "path": "War/Content/Blueprints/Structures/Forts/BPATGunAIT2.uasset",
      "properties": { "hpState": "normal", "mudLevel": 0.0, "snowLevel": 0.0 }
    },
    {
      "id": "flag",
      "path": "War/Content/Meshes/Props/Flag01.Flag01",
      "attachTo": {
        "parentId": "root",
        "socket": "FlagSocket",            // attach to named socket if present
        "offset": { "translation": [0,0,0.2], "rotation": [0,15,0], "scale": [1,1,1] }
      }
    }
  ],
  "filters": {
    "includePathContains": ["FortTrenches"],
    "excludeTags": ["EditorOnly"],
    "showMaterials": ["Wood", "Sandbag"],     // optional visibility control
    "hideMaterials": ["Dirt"]
  },
  "camera": { "pitch": 55, "yaw": 45, "angles": 4, "orbit": 0, "width": 2560, "height": 1444, "transparent": false },
  "render": { "output": "output/renders" }
}
```

Fields (high‑level)
- `assets[]`
  - `id`: string handle used by attachments
  - `path`: UE object path (e.g., `War/Content/.../MyMesh.MyMesh` or `...uasset`)
  - `metadataPath` (optional): separate source for anchor/socket lookups
  - `type` (optional): `"blueprint" | "mesh" | "skeletal_mesh"` (usually inferred)
  - `properties` (optional): `{ hpState, colorVariant, colorMaterialIndex, mudLevel, snowLevel, stockpile }`
  - `attachTo`: (optional)`{ parentId, anchor?, socket?, offset? }`
- `filters` (optional)
  - `includePathContains[]`, `excludePathContains[]`, `includeTags[]`, `excludeTags[]`
  - `showMaterials[]`, `hideMaterials[]`
- `camera` (optional)
  - `pitch`, `yaw`, `orbit`, `angles`, `width`, `height`, `transparent`
- `render` (optional)
  - `output`: destination directory
  - `postProcess` (experimental; requires `HEADLESS_ENABLE_PP=1`)

See `Cli/SceneSpec.cs` for exact JSON property names.

## Commands

The root command is `FModelHeadless` (invoked via `dotnet run` in the scripts). Top sub‑commands:
- `render`  Render meshes from a scene JSON file
- `blueprint`  Blueprint helpers
- `cargo`  Attachment helpers (anchors / transfer transforms)
- `lighting`  Sample lighting data
- `variants`  Variant & overlay helpers
- `search`  Search mounted virtual paths

Examples
```bash
# Explicit pak dir
scripts/render-windows.sh render \
  --pak-dir "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Foxhole\\War\\Content\\Paks" \
  --game-version GAME_UE4_24 \
  --scene output/scene_bpatgunait2.json

# Search for assets containing a token
scripts/render-windows.sh search --token FortT2Floor01
```

## Experimental Post‑Processing (opt‑in)

We ship an optional post shader (vignette, grain, simple chromatic fringe, dirt‑mask proxy). Because some headless OpenGL stacks zero the backbuffer, PP is disabled by default. Readback uses the pre‑PP resolved buffer for stability.

Enable (only if you want to experiment)
```bash
HEADLESS_ENABLE_PP=1 scripts/render-windows.sh render --scene output/scene_pp_static.json --game-version GAME_UE4_24 --verbose
```
- JSON: `render.postProcess.enabled: true` plus overrides (see `output/scene_pp_static.json`).
- If outputs are unexpectedly blank/transparent, unset `HEADLESS_ENABLE_PP` and re‑run.

## Troubleshooting

- Transparent PNGs or tiny files (~17 KB)
  - Ensure your scene has `"transparent": false` for the camera.
  - PP off by default; readback uses the resolved MSAA color FBO.
- Missing meshes/components
  - Some blueprint components may reference editor‑only objects; use `filters.include/exclude*` to control.
- White/flat materials
  - Material instance parent climb is implemented; if a specific MI still lacks textures, check verbose logs for parameters.
- Default pak path not found
  - Pass `--pak-dir` explicitly. The repo never bundles assets or AES keys.

## Development Notes

- Language: C# (.NET 8). OpenTK for GL. ImageSharp for PNG.
- Build flags
  - `CUE4PARSE_SKIP_NATIVE=1` (wrapper sets it)
  - `DOTNET_CONFIGURATION=Debug|Release` (affects wrapper)

## License

GNU General Public License v3.0 or later (see `LICENSE`).

## Acknowledgements

Huge thanks to the upstream projects that make this possible:

- CUE4Parse — Unreal Engine asset parsing library and tools.
  - Repository: https://github.com/FabianFG/CUE4Parse
- FModel — UE asset browser; this project ports parts of its viewer.
  - Repository: https://github.com/4sval/FModel
- Snooper — the FModel renderer/viewport we adapted (“SnooperPort/”).
  - Source lives inside the FModel repository (viewer module).
