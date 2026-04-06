import type { SearchDocsParams } from '../schemas/index.js'
import { fetchPage } from '../lib/fetcher.js'
import { ensureInitialized, search } from '../lib/search-index.js'

export async function searchDocs(params: SearchDocsParams): Promise<string> {
  // Lazy-initialize the search index on first query
  try {
    await ensureInitialized(fetchPage)
  } catch (err) {
    console.error('[arenula-docs] Search index initialization failed:', err)
    // Continue with whatever was indexed — partial results are better than none
  }

  const results = search(params.query, params.limit)

  if (results.length === 0) {
    return JSON.stringify({
      query: params.query,
      count: 0,
      results: [],
      hint: 'No indexed results. Try sbox_docs_get_page with a direct URL from docs.facepunch.com.',
    })
  }

  return JSON.stringify({
    query: params.query,
    count: results.length,
    results,
  })
}
