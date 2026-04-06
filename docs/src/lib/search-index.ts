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

// Key documentation pages to seed the search index
const SEED_URLS = [
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/about-QGgSEpJhxe', title: 'About' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/first-steps-7IyiSplYmn', title: 'First Steps' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/code-basics-3W3PHk1tD3', title: 'Code Basics' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/cheat-sheet-CH6MPz8N2j', title: 'Cheat Sheet' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/scenes-LT2kjsMBy4', title: 'Scenes' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/gameobject-oUVQQzT4IO', title: 'GameObject' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/components-zIujvXKpIl', title: 'Components' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/component-methods-OCvoNh8ByW', title: 'Component Methods' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/prefabs-Tiq5GBWmm3', title: 'Prefabs' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/networking-multiplayer-kaVboe3yRD', title: 'Networking & Multiplayer' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/input-Hhi5KoJOnF', title: 'Input' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/navigation-vwoSUsEPJ9', title: 'Navigation' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/tracing-FI4tuMSbSF', title: 'Tracing' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/terrain-RoH8crPRmG', title: 'Terrain' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/ui-kM9biZcQrj', title: 'UI' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/player-controller-G9xW4n1yAS', title: 'Player Controller' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/assetsresources-vfClHodkqi', title: 'Assets & Resources' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/particle-effect-Ah4PenyGKk', title: 'Particle Effects' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/shader-graph-O1KJlOQ8Pe', title: 'Shader Graph' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/post-processing-oRlAHNS6bK', title: 'Post Processing' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/razor-panels-dMbfl4Sqlw', title: 'Razor Panels' },
  { url: 'https://docs.facepunch.com/s/sbox-dev/doc/file-system-0LoS75PRwn', title: 'File System' },
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

    // Try each query word to find the best snippet anchor
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
      // No match in content — use the beginning as a fallback
      snippet = content.slice(0, 200).trim()
    }

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
