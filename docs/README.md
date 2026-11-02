# Wiki source

Start at [wiki/Home.md](wiki/Home.md). These English pages cover installation, optional mods and authoring against framework 0.8.21/API 0.8.11.

`wiki/` is source Markdown suitable for a normal repository. Export for a GitHub Wiki:

```powershell
./docs/Export-Wiki.ps1 -Destination './wiki-export' -LinkStyle Wiki
```

Use `-LinkStyle Repository` to retain `.md` links. Export only creates a fresh folder. It does not publish, commit or push. See [Publishing the Wiki](wiki/Publishing-the-Wiki.md).

In a launcher bundle, the same files are under `documentation/`; run `./documentation/Export-Wiki.ps1` from the bundle root instead.
