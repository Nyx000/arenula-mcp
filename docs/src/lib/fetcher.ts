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
  $('nav, .sidebar, footer, .header, .nav, script, style, .breadcrumb, .toc').remove()

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
  return { title, markdown, url }
}

export async function fetchApiType(typeName: string): Promise<FetchResult> {
  // s&box API docs are at docs.facepunch.com — try the API reference page
  const url = `https://docs.facepunch.com/s/sbox-dev/doc/api/${encodeURIComponent(typeName)}`
  try {
    return await fetchPage(url)
  } catch {
    // Fallback: try searching the docs
    throw new Error(
      `Could not fetch API docs for '${typeName}'. ` +
      `Use arenula-api's get_type tool for structured API data instead.`
    )
  }
}
