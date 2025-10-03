# FoxholeModel

Headless CLI tools for inspecting, exporting and rendering Unreal Engine (UE4) assets – tuned for the Foxhole game data – using CUE4Parse and a lightly ported FModel renderer. Runs in a hidden OpenGL context and exports PNG screenshots from a JSON scene description.

- Export of models and sounds from pak archives
- Static + skeletal meshes (including blueprint‑discovered components)
- Blueprint traversal: SCS/ICH templates, ChildActorComponents, default‑object properties (e.g., `SkelMesh`, `FlagMesh`)
- Component material overrides, MIC parent climb, vertex color overrides
- Socket attachments and transform offsets (sometimes)
- Scene filtering (path/tags) and material visibility (show/hide by token)
- Cross‑platform convenience: WSL→PowerShell wrapper for Windows Foxhole installs

This repository is GPL‑3.0 (see `LICENSE`). Do not commit proprietary game data or keys.

## Requirements & Quick Start

Windows (native or via WSL wrapper)
- Install .NET 8 SDK.
- Default pak path (if not given): `C:\Program Files (x86)\Steam\steamapps\common\Foxhole\War\Content\Paks`.
- Build: `dotnet build -c Release` (or use `scripts/build-windows.sh -c Release`).
- Run (wrapper forwards arguments):
  ```bash
  scripts/render-windows.sh render --game-version GAME_UE4_24 --scene output/scene_bpatgunait2.json --verbose
  ```

Linux (Arch/Ubuntu examples)
- Install: .NET 8 SDK/runtime and Mesa OpenGL stack.
  - Arch: `pacman -S --needed git dotnet-sdk-8.0 dotnet-runtime-8.0 mesa libglvnd`
  - Debian/Ubuntu: `sudo apt install -y git dotnet-sdk-8.0 mesa-utils libgl1-mesa-dri libegl1`
- Build (skips CUE4Parse natives by default):
  ```bash
  ./scripts/build.sh -c Release
  ```
- Optional software GL (llvmpipe) if no GPU context:
  ```bash
  export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe MESA_LOADER_DRIVER_OVERRIDE=swrast \
         MESA_GL_VERSION_OVERRIDE=4.5 MESA_GLSL_VERSION_OVERRIDE=450
  ```
- Run:
  ```bash
  dotnet run -c Release -- render --scene output/scene_truck.json --game-version GAME_UE4_24 --verbose
  ```

Publish a standalone binary (Linux)
```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/linux-x64
sudo ln -sf "$(pwd)/dist/linux-x64/FModelHeadless" /usr/local/bin/fmodel-headless
```

Pak discovery
- If `--pak-dir` is omitted, the CLI probes common Steam locations (Windows and `/mnt/c/...` under WSL).

Environment
- `CUE4PARSE_SKIP_NATIVE=1` skips native helpers (default in our build scripts). Recommended for portability.
- `HEADLESS_ENABLE_PP=1` enables optional post‑processing defined in scene JSON.

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
- `export` Export various assets

Tip: append `--help` to any command or subcommand (e.g., `render --help`, `export --help`) to list all available options and usage.

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
