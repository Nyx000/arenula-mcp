import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { homedir } from 'node:os'
import type { SboxApiData, CacheConfig } from '../types.js'

const CACHE_DIR = process.env.SBOX_CACHE_DIR || join(homedir(), '.arenula-api')
const CONFIG_PATH = join(CACHE_DIR, 'config.json')
const DATA_PATH = join(CACHE_DIR, 'api-data.json')

// Release: 2026-04-20-19-10-25 — bump via `npm run bump-cdn <YYYY-MM-DD-HH-MM-SS>`
const FALLBACK_URL = process.env.SBOX_API_URL || 'https://cdn.sbox.game/releases/2026-04-20-19-10-25.zip.json'

function ensureCacheDir(): void {
  if (!existsSync(CACHE_DIR)) {
    mkdirSync(CACHE_DIR, { recursive: true })
  }
}

function readConfig(): CacheConfig {
  if (existsSync(CONFIG_PATH)) {
    return JSON.parse(readFileSync(CONFIG_PATH, 'utf-8'))
  }
  return { url: FALLBACK_URL }
}

function writeConfig(config: CacheConfig): void {
  ensureCacheDir()
  writeFileSync(CONFIG_PATH, JSON.stringify(config, null, 2))
}

async function downloadApi(url: string, currentEtag?: string): Promise<{ data: string; etag?: string } | null> {
  // Check ETag with HEAD request first if we have a cached version
  if (currentEtag) {
    try {
      const headRes = await fetch(url, { method: 'HEAD' })
      const remoteEtag = headRes.headers.get('etag')
      if (remoteEtag && remoteEtag === currentEtag) {
        console.error('[arenula-api] Cache is up to date (ETag match)')
        return null // No update needed
      }
    } catch {
      console.error('[arenula-api] HEAD request failed, will try full download')
    }
  }

  console.error(`[arenula-api] Downloading API data from ${url}...`)
  const res = await fetch(url)
  if (!res.ok) {
    throw new Error(`Failed to download: ${res.status} ${res.statusText}`)
  }

  const data = await res.text()
  const etag = res.headers.get('etag') || undefined
  console.error(`[arenula-api] Downloaded ${(data.length / 1024 / 1024).toFixed(1)} MB`)
  return { data, etag }
}

export async function loadApiData(): Promise<SboxApiData> {
  ensureCacheDir()
  const config = readConfig()

  // Try to download or use cache
  const hasCachedData = existsSync(DATA_PATH)

  try {
    const result = await downloadApi(config.url, config.etag)

    if (result) {
      // New data downloaded
      writeFileSync(DATA_PATH, result.data)
      writeConfig({
        url: config.url,
        etag: result.etag,
        lastFetched: new Date().toISOString(),
      })
      const parsed: SboxApiData = JSON.parse(result.data)
      console.error(`[arenula-api] Loaded ${parsed.Types.length} types`)
      return parsed
    }
  } catch (err) {
    console.error(`[arenula-api] Download error: ${err}`)
  }

  // Use cached data
  if (hasCachedData) {
    console.error('[arenula-api] Using cached data')
    const raw = readFileSync(DATA_PATH, 'utf-8')
    const parsed: SboxApiData = JSON.parse(raw)
    console.error(`[arenula-api] Loaded ${parsed.Types.length} types from cache`)
    return parsed
  }

  throw new Error(
    '[arenula-api] No cached data and download failed. Please check your network or provide the URL manually.'
  )
}

export async function updateApiSource(newUrl: string): Promise<SboxApiData> {
  ensureCacheDir()

  const result = await downloadApi(newUrl)
  if (!result) {
    throw new Error('Download returned no data')
  }

  writeFileSync(DATA_PATH, result.data)
  writeConfig({
    url: newUrl,
    etag: result.etag,
    lastFetched: new Date().toISOString(),
  })

  const parsed: SboxApiData = JSON.parse(result.data)
  console.error(`[arenula-api] Updated to new source: ${parsed.Types.length} types`)
  return parsed
}
