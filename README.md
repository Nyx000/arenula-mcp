# Arenula (retired)

This project is retired and the repository is archived. Use the MCP server that ships with s&box instead: see the official [MCP Server docs](https://sbox.game/dev/doc/editor/mcp-server).

## Why

The s&box editor has shipped its own MCP server since engine build 26.07.08. Since build 26.10.13, agents connect to it through `sbox-cli`, which ships with every s&box install in `<engine>/bin/managed/`. Its tools cover scenes, components, assets, Hammer maps, play mode, the console, compile results and screenshots, and projects can add their own tools with the `[McpTool]` attribute, so this project's editor plugin is no longer needed.

## Moving over

1. Remove this project's entries from your MCP client config (`.mcp.json` in your s&box project, or wherever you added them): `editor` (SSE on `http://localhost:8098/sse`), `api` and `docs`.
2. Delete the editor plugin from your project: `Libraries/arenula_mcp/`.
3. Register `sbox-cli` with your client, from your project folder, while the editor has the project open. For Claude Code:

   ```
   claude mcp add sbox -- "<engine>/bin/managed/sbox-cli.exe" --connect
   ```

   `<engine>` is the folder containing `sbox.exe`. Other MCP clients run the same command as a stdio server. The editor accepts connections unless **Project Settings > AI > Enable MCP Communication** is off.
4. Only a few entry points are listed up front; agents find the rest with `search_tools`.

For the agent instructions that ship with the engine, run `sbox-cli --skill`. The [docs page](https://sbox.game/dev/doc/editor/mcp-server) covers writing your own tools; at the time of writing, its connection steps still describe the fixed-port HTTP server that build 26.10.13 replaced with `sbox-cli`.

## Where each part went

| This repo offered | Use now |
|---|---|
| Editor tools (`editor/`) | The first-party MCP server, through `sbox-cli` |
| Docs server (`docs/`) | The official docs, which serve markdown; index at <https://sbox.game/llms.txt> |
| API server (`api/`) | The official API reference at <https://sbox.game/api> |
| Schema generator (`schemagen/`) | Stays in this repo, unmaintained |

## The code

The code stays here, unmaintained, for reference, under the existing licences (GPL-3.0 for the editor plugin, MIT for the API, docs and schemagen; see [`LICENSE`](LICENSE), which also records where each part came from).

The previous README is in the git history at `d61fa75`: [README.md at d61fa75](https://github.com/Nyx000/arenula-mcp/blob/d61fa75/README.md).

## Credits

- Editor based on [Ozmium MCP Server](https://github.com/ozmium7/ozmium-mcp-server-for-sbox) by ozmium7 (GPL-3.0)
- API based on [sbox-api-mcp](https://github.com/sofianebel/sbox-api-mcp) by sofianebel (MIT)
- Docs based on [sbox-docs-mcp](https://github.com/sofianebel/sbox-docs-mcp) by sofianebel (MIT)

Licence terms for each part: [`LICENSE`](LICENSE).
