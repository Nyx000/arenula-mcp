# Arenula API

Offline s&box type/member reference for AI coding assistants. Indexes ~1,800 types and ~15,000 members from the s&box CDN.

Part of the [Arenula MCP suite](../README.md). Based on [sbox-api-mcp](https://github.com/sofianebel/sbox-api-mcp) by sofianebel (MIT).

## Install & Run

```bash
cd api
npm install
npm run build
node dist/index.js
```

## Tools (9)

| Tool | Purpose |
|---|---|
| `search_types` | Fuzzy search for classes, structs, enums, interfaces |
| `get_type` | Full type detail (methods, properties, fields) |
| `search_members` | Search methods/properties across all types |
| `list_namespaces` | All API namespaces with type counts |
| `search_docs` | Full-text search across documentation summaries |
| `update_api_source` | Re-download API data from a new CDN URL |
| `search_packages` | Search s&box Workshop/UGC |
| `get_package` | Get Workshop package details |
| `textures` | Search, download, and generate .tmat files from PBR texture providers (ambientCG, Poly Haven) |

## Updating API Data

Set `SBOX_API_URL` environment variable to the latest CDN schema URL from https://sbox.game/api/schema.
