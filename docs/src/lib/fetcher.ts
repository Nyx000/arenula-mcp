import * as cheerio from 'cheerio'
import TurndownService from 'turndown'

const turndown = new TurndownService({
  headingStyle: 'atx',
  codeBlockStyle: 'fenced',
})

const TIMEOUT = parseInt(process.env.SBOX_DOCS_REQUEST_TIMEOUT || '10000')
const USER_AGENT = process.env.SBOX_DOCS_USER_AGENT || 'arenula-docs/1.0.0'

export interface FetchResult {
  markdown: string
  title: string
  url: string
}

export async function fetchPage(url: string): Promise<FetchResult> {
  const response = await fetch(url, {
    headers: { 'User-Agent': USER_AGENT },
    signal: AbortSignal.timeout(TIMEOUT),
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${response.statusText}`)
  }

  const html = await response.text()
  const $ = cheerio.load(html)

  // Remove non-content elements
  $('nav, .sidebar, footer, .header, .nav, script, style, .breadcrumb, .toc, .menu, .toolbar, .pagination, iframe, noscript').remove()

  const title = $('h1').first().text().trim() || $('title').text().trim() || 'Untitled'

  // Try multiple content selectors — Facepunch docs use different layouts
  const contentSelectors = ['.document-content', '.content', '.article-content', 'main', 'article', '.page-content']
  let rawHtml = ''
  for (const selector of contentSelectors) {
    const el = $(selector).first()
    if (el.length > 0) {
      rawHtml = el.html() || ''
      break
    }
  }
  if (!rawHtml) {
    rawHtml = $('body').html() || ''
  }

  const markdown = turndown.turndown(rawHtml).trim()
  if (!markdown) {
    throw new Error(`No content extracted from ${url}`)
  }

  return { title, markdown, url }
}

export async function fetchApiType(typeName: string): Promise<FetchResult> {
  // Facepunch docs URLs include a hash suffix (e.g., gameobject-oUVQQzT4IO).
  // Look up the correct URL from the sitemap.
  const slug = typeName.toLowerCase().replace(/[^a-z0-9]+/g, '-')
  try {
    const sitemapRes = await fetch('https://docs.facepunch.com/api/shares.sitemap?id=sbox-dev', {
      headers: { 'User-Agent': USER_AGENT },
      signal: AbortSignal.timeout(TIMEOUT),
    })
    const xml = await sitemapRes.text()
    const pattern = new RegExp(`https://docs\\.facepunch\\.com/s/sbox-dev/doc/${slug}-[A-Za-z0-9]+`)
    const match = xml.match(pattern)
    if (match) {
      return await fetchPage(match[0])
    }
  } catch {
    // Sitemap lookup failed — fall through to error
  }

  throw new Error(
    `No docs page found for '${typeName}'. ` +
    `Try sbox_docs_search to find the relevant page, ` +
    `or use arenula-api's get_type tool for structured API data.`
  )
}
