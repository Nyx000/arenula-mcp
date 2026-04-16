import MiniSearch from 'minisearch'

export interface DocEntry {
  id: string
  title: string
  content: string
  url: string
  category: string
  enriched: boolean
}

export interface SearchResult {
  title: string
  url: string
  snippet: string
  category: string
  indexed: boolean
}

export interface PageInfo {
  title: string
  url: string
  category: string
}

const SBOX_BASE = 'https://sbox.game'
const LLMS_TXT_URL = `${SBOX_BASE}/llms.txt`
const TIMEOUT = parseInt(process.env.SBOX_DOCS_REQUEST_TIMEOUT || '10000')
const USER_AGENT = process.env.SBOX_DOCS_USER_AGENT || 'arenula-docs/1.0.0'

const index = new MiniSearch<DocEntry>({
  fields: ['title', 'content'],
  storeFields: ['title', 'url', 'content', 'category', 'enriched'],
  searchOptions: {
    boost: { title: 2 },
    fuzzy: 0.2,
    prefix: true,
  },
})

let pages: PageInfo[] = []
let categories: string[] = []
let initialized = false

/**
 * Parse llms.txt into pages with categories.
 * Format: `## Category` headings followed by `- [Title](/path.md)` entries.
 */
export function parseLlmsTxt(text: string): { pages: PageInfo[]; categories: string[] } {
  const result: PageInfo[] = []
  const cats: string[] = []
  let currentCategory = 'Uncategorized'

  for (const line of text.split('\n')) {
    const sectionMatch = line.match(/^##\s+(.+)/)
    if (sectionMatch) {
      currentCategory = sectionMatch[1].trim()
      cats.push(currentCategory)
      continue
    }
    const entryMatch = line.match(/^- \[([^\]]+)\]\(([^)]+\.md)\)/)
    if (entryMatch) {
      result.push({
        title: entryMatch[1],
        url: `${SBOX_BASE}${entryMatch[2]}`,
        category: currentCategory,
      })
    }
  }

  return { pages: result, categories: cats }
}

/**
 * Fetch llms.txt and seed the index with title-only entries.
 * Fast — one HTTP request, no page content fetched.
 */
export async function ensureInitialized(): Promise<void> {
  if (initialized) return
  initialized = true

  try {
    const response = await fetch(LLMS_TXT_URL, {
      headers: { 'User-Agent': USER_AGENT },
      signal: AbortSignal.timeout(TIMEOUT),
    })
    if (!response.ok) {
      console.error(`[arenula-docs] Failed to fetch llms.txt: HTTP ${response.status}`)
      return
    }

    const text = await response.text()
    const parsed = parseLlmsTxt(text)
    pages = parsed.pages
    categories = parsed.categories

    for (const page of pages) {
      index.add({
        id: page.url,
        title: page.title,
        content: page.title,
        url: page.url,
        category: page.category,
        enriched: false,
      })
    }

    console.error(`[arenula-docs] Indexed ${pages.length} pages from llms.txt (title-only)`)
  } catch (err) {
    console.error('[arenula-docs] Failed to initialize search index:', err)
  }
}

/**
 * Replace a title-only stub with full page content, or add a new entry.
 */
export function updateDocument(url: string, title: string, content: string): void {
  const page = pages.find(p => p.url === url)
  const category = page?.category || 'Uncategorized'

  const entry: DocEntry = {
    id: url,
    title,
    content,
    url,
    category,
    enriched: true,
  }

  if (index.has(url)) {
    index.replace(entry)
  } else {
    index.add(entry)
    if (!page) {
      pages.push({ title, url, category })
    }
  }
}

export function search(query: string, limit = 10): SearchResult[] {
  const results = index.search(query).slice(0, limit)

  return results.map(r => {
    const content = (r as unknown as { content: string }).content || ''
    const category = (r as unknown as { category: string }).category || ''
    const enriched = (r as unknown as { enriched: boolean }).enriched ?? false

    if (!enriched) {
      return {
        title: r.title as string,
        url: r.url as string,
        snippet: `[${category}] — fetch page for full content`,
        category,
        indexed: false,
      }
    }

    const lowerContent = content.toLowerCase()
    const queryWords = query.toLowerCase().split(/\s+/).filter(Boolean)
    let matchIdx = -1
    for (const word of queryWords) {
      matchIdx = lowerContent.indexOf(word)
      if (matchIdx !== -1) break
    }

    let snippet: string
    if (matchIdx !== -1) {
      const snippetStart = Math.max(0, matchIdx - 50)
      snippet = content.slice(snippetStart, snippetStart + 200).trim()
    } else {
      snippet = content.slice(0, 200).trim()
    }

    return {
      title: r.title as string,
      url: r.url as string,
      snippet: snippet ? `...${snippet}...` : '',
      category,
      indexed: true,
    }
  })
}

export function getPages(): PageInfo[] {
  return pages
}

export function getCategories(): string[] {
  return categories
}

export function isInitialized(): boolean {
  return initialized
}
