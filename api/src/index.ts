#!/usr/bin/env node
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'
import { loadApiData } from './data/loader.js'
import { buildIndexes } from './data/indexer.js'
import { registerSearchTypes } from './tools/search-types.js'
import { registerGetType } from './tools/get-type.js'
import { registerSearchMembers } from './tools/search-members.js'
import { registerListNamespaces } from './tools/list-namespaces.js'
import { registerSearchDocs } from './tools/search-docs.js'
import { registerUpdateSource } from './tools/update-source.js'
import { registerSearchPackages } from './tools/search-packages.js'
import { registerGetPackage } from './tools/get-package.js'

async function main() {
  console.error('[arenula-api] Starting S&box API MCP Server...')

  // Load API data
  const data = await loadApiData()
  const indexes = buildIndexes(data)

  // Create MCP server
  const server = new McpServer({
    name: 'Arenula API',
    version: '1.0.0',
  })

  // Register all tools
  registerSearchTypes(server, indexes)
  registerGetType(server, indexes)
  registerSearchMembers(server, indexes)
  registerListNamespaces(server, indexes)
  registerSearchDocs(server, indexes)
  registerUpdateSource(server, indexes)

  // UGC Workshop tools (no indexes needed - live API)
  registerSearchPackages(server)
  registerGetPackage(server)

  // Connect via stdio
  const transport = new StdioServerTransport()
  await server.connect(transport)
  console.error('[arenula-api] Server running on stdio')
}

main().catch(err => {
  console.error('[arenula-api] Fatal error:', err)
  process.exit(1)
})
