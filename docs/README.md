# Arenula Docs

Fetches narrative s&box documentation from docs.facepunch.com for AI coding assistants. Scrapes HTML, converts to Markdown, caches with LRU.

Part of the [Arenula MCP suite](../README.md). Based on [sbox-docs-mcp](https://github.com/sofianebel/sbox-docs-mcp) by sofianebel (MIT).

## Install & Run

```bash
cd docs
npm install
npm run build
node dist/index.js
```

## Tools (3)

| Tool | Purpose |
|---|---|
| `sbox_docs_search` | Search indexed doc pages by keyword |
| `sbox_docs_get_page` | Fetch any docs.facepunch.com URL as Markdown |
| `sbox_api_get_type` | Fetch API type docs from Facepunch docs |
