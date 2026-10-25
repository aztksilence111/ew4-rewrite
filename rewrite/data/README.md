# Runtime Content

`rewrite/` contains the API 3 extension manifest and reusable geography rules.
The built-in East Asia map uses 121 impassable mountain cells; the two barrier
types and default atlas sprites are available to every registered map and Mod.

Everything needed to run and package the current rewrite is stored under
`rewrite/`. The preserved APK workspace and reverse-engineering reports are
not runtime or packaging inputs.

- `original/`: the original 95 scenarios, terrain maps and XML/INI definitions,
  plus the reviewed East Asia BIN and geographic label positions. Map and city
  catalogs include East Asia; Europe, America and the scenario list are unchanged.
- `legacy/`: independently editable rule sections for commanders, skills, units,
  variants, items, recruitment, buildings, terrain, installations, conquest
  metadata, combat, progression and economy. The three old filenames are now
  compatibility indexes. See `../mod-kit/docs/porting/SPLIT_CATALOG.md`.
  Its `animations/` directory is an intermediate research export, not a
  packaging input.
- `client/`: the converted maps, textures, fonts, animations, sounds, UI and
  frontend manifests used by the renderer.
- `runtime-content.json`: sizes and SHA-256 hashes for all packaged runtime
  inputs in `original/`, `client/` and all top-level runtime `legacy/` JSON files.

East Asia is built in alongside Europe and America. Its 278 settlements,
terrain, rivers, crossings and bilingual labels are available to future
scenarios; no formal East Asia campaign or conquest is added. Authoring inputs
and reproducible generation tools are documented in `maps/eastasia_rewrite`.
`original/eastasia.map.json` is the editable, politically neutral settlement
and crossing template. As in the original format, the BIN stores terrain;
scenario authors must include the desired cities, facilities and crossings
in their BTL site records. The client map directory also contains the city
and facility placement tables.

The logical reader names `original/...` and `data/...` are retained for Android
compatibility. They now resolve to the files inside this directory. Moving
the files did not change source fingerprints or existing save formats.

## Web Publish

Run from `rewrite/`:

```powershell
dotnet publish Ew4.Web/Ew4.Web.csproj -c Release -o local/web-publish
Set-Location local/web-publish
dotnet Ew4.Web.dll --urls http://127.0.0.1:5184/
```

The publish directory contains the assemblies, `wwwroot/` and `data/`.
Default saves are written to `saves/` in that directory; use `--save-root`
to select a persistent profile. Existing development profiles do not move
automatically. The active preview explicitly retains its existing profile.

For development, the existing `--root <repository>` option resolves data to
`<repository>/rewrite/data`. `--data-root <directory>` can select a different
complete data bundle. Neither mode reads `apk_work` or `reverse/reports`.

## Android

`Ew4.Android.csproj` includes only frontend files and data inside `rewrite/`.
The original logical paths in the APK are unchanged. Updating this resource
configuration does not build or update an APK.

## Provenance And Verification

`tools/migrate_rewrite_content.py` in the repository is the one-time import
utility; it is not invoked by the build. It preserves existing destinations
that differ from the original. Original materials remain unchanged.

`tools/verify_rewrite_bundle.cjs` checks imported hashes, published file bytes,
Android asset source paths and portable server startup/save compatibility.
Native reverse-engineering comparison tools may still read the original
workspace deliberately; they are not part of application packaging.
