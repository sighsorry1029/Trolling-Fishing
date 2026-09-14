# Vendored ServerSync

`ServerSync.dll` is the locally prepared `valheim-1.0.7-r1` compatibility build, pinned
here for ILRepack. It is not installed as a standalone plugin.

- Upstream: https://github.com/blaxxun-boop/ServerSync/tree/c57c2aa54e07cdcc7630d6068699ea781622323e
- License: MIT-0; see `ServerSync.LICENSE.txt`.
- Assembly version: 1.0.0.0; file version: 1.0.0.1.
- SHA-256: `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`.
- Preserved source, build inputs, patches and validation:
  `C:/Users/blizz/.codex/references/valheim/integrations/serversync/versions/valheim-1.0.7-r1/`.
  The input is that directory's `lib/net48/ServerSync.dll`, checked against `manifest.json`.

The compatibility changes compile `ZRoutedRpc.Everybody` as the new constant, use the
public `ZNet.IsAdmin` API, and keep player/history/admin lists and network time behind
buffered PeerInfo during initial configuration transfer. Upstream public API, configuration
identifiers, authority policy and wire format are retained. See the shared source and
verification reports for scope; real host/dedicated and Steam/PlayFab sessions remain required.

This is a fixed build input. Building TrollingFishing does not replace it from a global
latest path or rebuild other mods. YamlDotNet remains supplied by the project's pinned NuGet version.
