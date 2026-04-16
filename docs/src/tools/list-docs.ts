import type { ListDocsParams } from '../schemas/index.js'
import { ensureInitialized, getPages, getCategories } from '../lib/search-index.js'

export async function listDocs(params: ListDocsParams): Promise<string> {
  await ensureInitialized()

  const allPages = getPages()
  const allCategories = getCategories()
  const { category, limit, offset } = params

  const filtered = category
    ? allPages.filter(p => p.category.toLowerCase() === category.toLowerCase())
    : allPages

  const paginated = filtered.slice(offset, offset + limit)
  const hasMore = offset + limit < filtered.length

  const categorySummary = allCategories.map(cat => ({
    name: cat,
    count: allPages.filter(p => p.category === cat).length,
  }))

  return JSON.stringify({
    categories: categorySummary,
    total: filtered.length,
    offset,
    hasMore,
    results: paginated.map(p => ({
      title: p.title,
      url: p.url,
      category: p.category,
    })),
  })
}
