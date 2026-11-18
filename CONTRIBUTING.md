# Contributing / Como contribuir

Thanks for helping! This is a small fan project, so the rules are simple. · Obrigado por ajudar! É um projeto pequeno de fãs,
então as regras são simples.

## Ways to help / Jeitos de ajudar

- **Report a bug** with the [bug report form](../../issues/new/choose). For install problems, attach the file written by
  `Diagnose-ADOFAI-Online.bat`.
- **Translate the mod** into your language without touching the code: copy `BepInEx/config/AdofaiOnline-lang/template.txt`
  to `<code>.txt` (for example `es.txt`), translate the right side of each line and add `# name: Your language`. Send the file in a pull
  request (put it in `mod/lang/`) or in an issue. See "Add your own language" in [`mod/README.md`](mod/README.md).
- **Add words to the chat filter** with `BepInEx/config/AdofaiOnline-lang/filter.txt` (one per line).
- **Host a room server** for your community: see [`server/node/README.md`](server/node/README.md) (a small VM) or
  `server/cloudflare` (Cloudflare Workers, free plan). Everyone in a room must use the same server.
- **Send code**: bug fixes and small features are welcome. Open an issue first for anything big.

## Project layout / Estrutura

| Folder | What it is |
|---|---|
| `mod/` | The BepInEx plugin: patches (`src/Plugin.cs`), shims, installer scripts, README |
| `shared/` | The online code: client, screens, localization, options |
| `server/node/` | Room server for a VM (`server.mjs`) |
| `server/cloudflare/` | The same protocol as a Cloudflare Worker |

## Building / Compilar

You need the game with **BepInEx 5** installed and a Unity **6000.3** editor (only for its C# compiler). From the repository root:

```bash
bash mod/build.sh
```

The script reads the game's DLLs from its Steam folder (set `ADOFAI_GAME` if it is not in the default place) and writes
`AdofaiOnlineMod.dll`. Copy it to `BepInEx/plugins` to test. A quick way to check the installation is
`Diagnose-ADOFAI-Online.bat`.

## Conventions / Convenções

- **Texts are written in Portuguese in the code** and translated by `OnlineLoc.cs` (English) and `OnlineLocKo.cs` (Korean).
  A new text needs an entry in both, or say in the pull request that it still needs a translation.
- **Never assign `Text.text` directly** on the online screens; use `PortUi.SetText` (it avoids a one-frame flash of the
  untranslated text).
- **A protocol change must be made in both servers** (Node and Cloudflare) and keep working with older clients: unknown
  messages are ignored.
- Keep privacy in mind: the servers never write IP addresses to disk or to their logs (the Node server keeps them in memory
  only while a room exists, for bans and rate limits; the Cloudflare server keeps short hashes) and the public `/stats` shows only
  numbers. See "What the room server sees" in [`mod/README.md`](mod/README.md).
- Versions: each release raises only the **last number** (0.2.8 → 0.2.9).

## Pull requests

1. Fork, make a branch, change one thing at a time.
2. Fill in the pull request template.
3. Do not add game files, builds, keys or personal data. This repository must stay free of anything that belongs to the game.

By contributing you agree that your work is released under the repository [license](LICENSE).
