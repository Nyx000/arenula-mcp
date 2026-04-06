import type { GetApiTypeParams } from '../schemas/index.js'
import { fetchApiType } from '../lib/fetcher.js'
import { Cache } from '../lib/cache.js'

const cache = new Cache<string>(
  parseInt(process.env.SBOX_DOCS_CACHE_TTL || '14400') * 1000,
  parseInt(process.env.SBOX_DOCS_MAX_CACHE_ENTRIES || '500'),
)

export async function getApiType(params: GetApiTypeParams): Promise<string> {
  const { type_name, include_methods, include_properties } = params
  const cacheKey = `api:${type_name}`

  // Check cache
  let markdown = cache.get(cacheKey)
  if (!markdown) {
    try {
      const result = await fetchApiType(type_name)
      markdown = result.markdown
      cache.set(cacheKey, markdown)
    } catch (err) {
      return JSON.stringify({
        type_name,
        error: err instanceof Error ? err.message : String(err),
        hint: 'Use arenula-api search_types/get_type tools for structured API data. This tool fetches narrative docs only.',
      })
    }
  }

  // Filter sections if requested
  let content = markdown
  if (!include_methods || !include_properties) {
    const lines = content.split('\n')
    const filtered: string[] = []
    let inMethodSection = false
    let inPropertySection = false

    for (const line of lines) {
      const lower = line.toLowerCase()
      if (lower.includes('## method') || lower.includes('### method')) {
        inMethodSection = true
        inPropertySection = false
      } else if (lower.includes('## propert') || lower.includes('### propert')) {
        inPropertySection = true
        inMethodSection = false
      } else if (line.startsWith('## ') || line.startsWith('### ')) {
        inMethodSection = false
        inPropertySection = false
      }

      const skip =
        (inMethodSection && !include_methods) ||
        (inPropertySection && !include_properties)

      if (!skip) filtered.push(line)
    }
    content = filtered.join('\n')
  }

  return JSON.stringify({
    type_name,
    content: content.slice(0, 10000),
    totalLength: content.length,
    truncated: content.length > 10000,
  })
}
