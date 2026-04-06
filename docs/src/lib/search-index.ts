import MiniSearch from 'minisearch'

export interface SearchResult {
  title: string
  url: string
  snippet: string
}

interface DocEntry {
  id: string
  title: string
  content: string
  url: string
}

const index = new MiniSearch<DocEntry>({
  fields: ['title', 'content'],
  storeFields: ['title', 'url', 'content'],
  searchOptions: {
    boost: { title: 2 },
    fuzzy: 0.2,
    prefix: true,
  },
})

let initialized = false

// Known documentation page URLs to seed the index
const SEED_URLS = [
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/components-hPnGbp7gHp', title: 'Components' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/game-objects-F7GKMBSH4q', title: 'Game Objects' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/scenes-gg4qKYgR3G', title: 'Scenes' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/networking-yrvYEwC7CP', title: 'Networking' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/ui-basics-vMbXBjK8T1', title: 'UI Basics' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/tracing-raycasting-hmmUGHmTOR', title: 'Tracing / Raycasting' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/navigation-vwoSUsEPJ9', title: 'Navigation' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/input-system-gMsPxPJc2z', title: 'Input System' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/sounds-music-6nPGaJyR8L', title: 'Sounds & Music' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/game-resources-Rf7k5fxS7e', title: 'Game Resources' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/terrain-UPxNq5rmfq', title: 'Terrain' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/physics-bodies-PKlBk3m2oL', title: 'Physics Bodies' },
]

export function addDocument(entry: DocEntry): void {
  if (!index.has(entry.id)) {
    index.add(entry)
  }
}

export async function ensureInitialized(fetcher: (url: string) => Promise<{ title: string; markdown: string }>): Promise<void> {
  if (initialized) return
  initialized = true

  // Seed the index with known docs pages — fetch in parallel with a concurrency limit
  const results = await Promise.allSettled(
    SEED_URLS.map(async ({ url, title }) => {
      try {
        const result = await fetcher(url)
        addDocument({
          id: url,
          title: result.title || title,
          content: result.markdown,
          url,
        })
      } catch {
        // Add a stub entry so search can still find the page by title
        addDocument({ id: url, title, content: title, url })
      }
    })
  )

  const succeeded = results.filter(r => r.status === 'fulfilled').length
  console.error(`[arenula-docs] Indexed ${succeeded}/${SEED_URLS.length} documentation pages`)
}

export function search(query: string, limit = 10): SearchResult[] {
  const results = index.search(query).slice(0, limit)
  return results.map(r => {
    const content = (r as unknown as { content: string }).content || ''
    // Extract a snippet around the first match
    const lowerContent = content.toLowerCase()
    const lowerQuery = query.toLowerCase().split(/\s+/)[0] || ''
    const matchIdx = lowerContent.indexOf(lowerQuery)
    const snippetStart = Math.max(0, matchIdx - 50)
    const snippet = content.slice(snippetStart, snippetStart + 200).trim()

    return {
      title: r.title as string,
      url: r.url as string,
      snippet: snippet ? `...${snippet}...` : '',
    }
  })
}

export function isInitialized(): boolean {
  return initialized
}
