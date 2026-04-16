const TIMEOUT = parseInt(process.env.SBOX_DOCS_REQUEST_TIMEOUT || '10000')
const USER_AGENT = process.env.SBOX_DOCS_USER_AGENT || 'arenula-docs/1.0.0'

const SBOX_BASE = 'https://sbox.game'

export interface FetchResult {
  markdown: string
  title: string
  url: string
}

/**
 * Normalize any docs URL to a sbox.game raw .md URL where possible.
 */
function toMarkdownUrl(url: string): string {
  if (url.startsWith(SBOX_BASE) && url.endsWith('.md')) return url

  if (url.startsWith(SBOX_BASE) && url.includes('/dev/doc/')) {
    return url.replace(/\/?$/, '.md')
  }

  // Legacy or unknown URLs — return as-is, fetchPage will try HTML fallback
  return url
}

/**
 * Extract a title from the first markdown heading, or fall back to the URL slug.
 */
function extractTitle(text: string, url: string): string {
  const headingMatch = text.match(/^#\s+(.+)$/m)
  if (headingMatch) return headingMatch[1].trim()

  const slug = url.replace(/\.md$/, '').split('/').pop() || 'Untitled'
  return slug.replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}

/**
 * Strip HTML tags and extract readable text. Targets content containers first,
 * falls back to full body. No external dependencies.
 */
function stripHtml(html: string): string {
  const containerMatch = html.match(
    /<(?:article|main)[^>]*>([\s\S]*?)<\/(?:article|main)>/i
  )
  const raw = containerMatch ? containerMatch[1] : html

  return raw
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<style[\s\S]*?<\/style>/gi, '')
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

export async function fetchPage(url: string): Promise<FetchResult> {
  const mdUrl = toMarkdownUrl(url)

  // Try raw .md endpoint first
  if (mdUrl.endsWith('.md') && mdUrl.startsWith(SBOX_BASE)) {
    try {
      const response = await fetch(mdUrl, {
        headers: { 'User-Agent': USER_AGENT },
        signal: AbortSignal.timeout(TIMEOUT),
      })

      if (response.ok) {
        const text = (await response.text()).trim()
        if (text && !text.trimStart().startsWith('<')) {
          return { title: extractTitle(text, mdUrl), markdown: text, url: mdUrl }
        }
      }
    } catch {
      // Fall through to original URL fetch
    }
  }

  // Fetch the original URL
  const response = await fetch(url, {
    headers: { 'User-Agent': USER_AGENT },
    signal: AbortSignal.timeout(TIMEOUT),
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${response.statusText}`)
  }

  const text = await response.text()

  // If it looks like markdown, use directly
  if (!text.trimStart().startsWith('<')) {
    return { title: extractTitle(text, url), markdown: text.trim(), url }
  }

  // HTML fallback — strip tags and extract text
  const stripped = stripHtml(text)
  if (stripped.length > 50) {
    return { title: extractTitle(stripped, url), markdown: stripped, url }
  }

  throw new Error(
    `Could not extract content from ${url}. ` +
    `Use sbox.game/dev/doc/... URLs with .md suffix for best results.`
  )
}
