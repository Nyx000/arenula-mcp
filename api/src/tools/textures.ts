import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { z } from 'zod'
import * as fs from 'fs'
import * as path from 'path'
import * as https from 'https'

// ── Types ──────────────────────────────────────────────────────────

interface TextureResult {
  id: string
  name: string
  provider: 'ambientcg' | 'polyhaven'
  downloadCount?: number
  categories?: string[]
  tags?: string[]
}

interface DownloadedTexture {
  slot: string
  assetPath: string
  filename: string
}

// ── Slot mapping ───────────────────────────────────────────────────

const AMBIENTCG_SLOT_MAP: Record<string, string> = {
  '_Color': 'AlbedoImage',
  '_NormalGL': 'NormalImage',
  '_Roughness': 'RoughnessImage',
  '_Displacement': 'HeightImage',
  '_AmbientOcclusion': 'AOImage',
}

const POLYHAVEN_SLOT_MAP: Record<string, string> = {
  'Diffuse': 'AlbedoImage',
  'nor_gl': 'NormalImage',
  'Rough': 'RoughnessImage',
  'Displacement': 'HeightImage',
  'AO': 'AOImage',
}

// ── HTTP helpers ───────────────────────────────────────────────────

function httpGet(url: string): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const doRequest = (reqUrl: string, redirects = 0) => {
      if (redirects > 5) return reject(new Error('Too many redirects'))
      const mod = reqUrl.startsWith('https') ? https : require('http')
      mod.get(reqUrl, { headers: { 'User-Agent': 'arenula-mcp/1.0' } }, (res: any) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          return doRequest(res.headers.location, redirects + 1)
        }
        if (res.statusCode !== 200) {
          return reject(new Error(`HTTP ${res.statusCode} for ${reqUrl}`))
        }
        const chunks: Buffer[] = []
        res.on('data', (c: Buffer) => chunks.push(c))
        res.on('end', () => resolve(Buffer.concat(chunks)))
        res.on('error', reject)
      }).on('error', reject)
    }
    doRequest(url)
  })
}

async function httpGetJson(url: string): Promise<any> {
  const buf = await httpGet(url)
  return JSON.parse(buf.toString('utf-8'))
}

// ── Project path resolution ────────────────────────────────────────

function resolveProjectRoot(): string {
  // CWD is the project root when launched from .mcp.json
  return process.cwd()
}

function ensureDir(dir: string) {
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true })
}

// ── ambientCG provider ─────────────────────────────────────────────

async function ambientcgSearch(query: string, limit: number): Promise<TextureResult[]> {
  // Try exact ID lookup first
  if (query[0]?.match(/[A-Z]/) && /\d/.test(query)) {
    const data = await httpGetJson(
      `https://ambientcg.com/api/v2/full_json?id=${encodeURIComponent(query)}&include=downloadData`
    )
    if (data.foundAssets?.length) {
      return data.foundAssets.map((a: any) => ({
        id: a.assetId,
        name: a.displayName || a.assetId,
        provider: 'ambientcg' as const,
        downloadCount: a.downloadCount,
        categories: a.categories,
        tags: a.tags,
      }))
    }
  }

  const params = new URLSearchParams({
    q: query,
    type: 'Material',
    limit: String(limit),
    include: 'downloadData',
    sort: 'Popular',
  })
  const data = await httpGetJson(`https://ambientcg.com/api/v2/full_json?${params}`)
  return (data.foundAssets || []).map((a: any) => ({
    id: a.assetId,
    name: a.displayName || a.assetId,
    provider: 'ambientcg' as const,
    downloadCount: a.downloadCount,
    categories: a.categories,
    tags: a.tags,
  }))
}

async function ambientcgDownload(
  assetId: string,
  resolution: string,
  projectRoot: string
): Promise<DownloadedTexture[]> {
  const url = `https://ambientcg.com/get?file=${assetId}_${resolution}-PNG.zip`
  const zipBuf = await httpGet(url)

  const texturesDir = path.join(projectRoot, 'Assets', 'terrain', 'textures', assetId)
  ensureDir(texturesDir)

  // Use Node's built-in zip handling via a simple ZIP parser
  const files = parseZipEntries(zipBuf)
  const downloaded: DownloadedTexture[] = []

  for (const [name, data] of files) {
    const ext = path.extname(name).toLowerCase()
    if (ext !== '.png' && ext !== '.jpg') continue
    if (name.includes('_NormalDX')) continue

    // Check if this maps to a .tmat slot
    let slot: string | undefined
    for (const [suffix, slotName] of Object.entries(AMBIENTCG_SLOT_MAP)) {
      if (name.includes(suffix)) {
        slot = slotName
        break
      }
    }
    if (!slot) continue

    const outPath = path.join(texturesDir, path.basename(name))
    fs.writeFileSync(outPath, data)

    const assetPath = `terrain/textures/${assetId}/${path.basename(name)}`
    downloaded.push({ slot, assetPath, filename: path.basename(name) })
  }

  return downloaded
}

// ── Poly Haven provider ────────────────────────────────────────────

async function polyhavenSearch(query: string, limit: number): Promise<TextureResult[]> {
  const data = await httpGetJson('https://api.polyhaven.com/assets?t=textures')
  const queryLower = query.toLowerCase()

  const matches: TextureResult[] = []
  for (const [id, asset] of Object.entries(data) as [string, any][]) {
    const nameMatch = id.includes(queryLower)
      || (asset.name || '').toLowerCase().includes(queryLower)
    const catMatch = asset.categories?.some((c: string) => c.toLowerCase().includes(queryLower))
    const tagMatch = asset.tags?.some((t: string) => t.toLowerCase().includes(queryLower))

    if (nameMatch || catMatch || tagMatch) {
      matches.push({
        id,
        name: asset.name || id,
        provider: 'polyhaven',
        downloadCount: asset.download_count,
        categories: asset.categories,
        tags: asset.tags,
      })
    }
    if (matches.length >= limit) break
  }

  // Sort by download count
  matches.sort((a, b) => (b.downloadCount || 0) - (a.downloadCount || 0))
  return matches.slice(0, limit)
}

async function polyhavenDownload(
  assetId: string,
  resolution: string,
  projectRoot: string
): Promise<DownloadedTexture[]> {
  const res = resolution.toLowerCase() // polyhaven uses lowercase: 1k, 2k, 4k
  const filesData = await httpGetJson(`https://api.polyhaven.com/files/${assetId}`)

  const texturesDir = path.join(projectRoot, 'Assets', 'terrain', 'textures', assetId)
  ensureDir(texturesDir)

  const downloaded: DownloadedTexture[] = []

  for (const [mapType, slotName] of Object.entries(POLYHAVEN_SLOT_MAP)) {
    const mapData = filesData[mapType]
    if (!mapData) continue

    const resData = mapData[res]
    if (!resData) continue

    // Prefer png, fall back to jpg
    const fmt = resData.png || resData.jpg
    if (!fmt?.url) continue

    const ext = resData.png ? 'png' : 'jpg'
    const filename = `${assetId}_${mapType}_${res}.${ext}`
    const outPath = path.join(texturesDir, filename)

    const buf = await httpGet(fmt.url)
    fs.writeFileSync(outPath, buf)

    const assetPath = `terrain/textures/${assetId}/${filename}`
    downloaded.push({ slot: slotName, assetPath, filename })
  }

  return downloaded
}

// ── .tmat generation ───────────────────────────────────────────────

function generateTmat(
  assetId: string,
  textures: DownloadedTexture[],
  projectRoot: string,
  uvScale: number
): string {
  const materialDir = path.join(projectRoot, 'Assets', 'terrain', 'material')
  ensureDir(materialDir)

  const tmat: Record<string, any> = {
    UVScale: uvScale,
    Metalness: 0,
    NormalStrength: 1,
    HeightBlendStrength: 1,
    DisplacementScale: 0,
    NoTiling: false,
    Surface: '',
  }

  for (const tex of textures) {
    tmat[tex.slot] = tex.assetPath
  }

  const tmatPath = path.join(materialDir, `${assetId.toLowerCase()}.tmat`)
  fs.writeFileSync(tmatPath, JSON.stringify(tmat, null, 2))

  return `terrain/material/${assetId.toLowerCase()}.tmat`
}

// ── Minimal ZIP parser (no external deps) ──────────────────────────

function parseZipEntries(buf: Buffer): [string, Buffer][] {
  const entries: [string, Buffer][] = []
  let offset = 0

  while (offset < buf.length - 4) {
    const sig = buf.readUInt32LE(offset)
    if (sig !== 0x04034b50) break // local file header signature

    const compressionMethod = buf.readUInt16LE(offset + 8)
    const compressedSize = buf.readUInt32LE(offset + 18)
    const uncompressedSize = buf.readUInt32LE(offset + 22)
    const nameLen = buf.readUInt16LE(offset + 26)
    const extraLen = buf.readUInt16LE(offset + 28)

    const name = buf.toString('utf-8', offset + 30, offset + 30 + nameLen)
    const dataStart = offset + 30 + nameLen + extraLen

    if (compressionMethod === 0 && uncompressedSize > 0) {
      // Stored (no compression)
      entries.push([name, buf.subarray(dataStart, dataStart + uncompressedSize)])
    } else if (compressionMethod === 8 && compressedSize > 0) {
      // Deflate — use zlib
      try {
        const zlib = require('zlib')
        const compressed = buf.subarray(dataStart, dataStart + compressedSize)
        const decompressed = zlib.inflateRawSync(compressed)
        entries.push([name, decompressed])
      } catch {
        // Skip files we can't decompress
      }
    }

    offset = dataStart + compressedSize
  }

  return entries
}

// ── Tool registration ──────────────────────────────────────────────

export function registerTextures(server: McpServer) {
  server.registerTool(
    'textures',
    {
      title: 'Fetch PBR Textures',
      description:
        'Search and download free CC0 PBR textures from ambientCG and Poly Haven. '
        + "Downloads PNGs to Assets/terrain/textures/ and generates s&box .tmat files. "
        + "Use 'search' to find textures, 'download' to fetch and wire up, 'list_providers' for info.",
      inputSchema: {
        action: z.enum(['search', 'download', 'list_providers']).describe(
          "Operation: 'search' finds textures, 'download' fetches + generates .tmat, 'list_providers' shows available sources"
        ),
        query: z.string().optional().describe(
          "Search term (e.g., 'rock', 'dirt', 'grass'). Required for: search"
        ),
        provider: z.enum(['ambientcg', 'polyhaven']).optional().describe(
          "Texture source. Default: ambientcg. Used by: search, download"
        ),
        asset_id: z.string().optional().describe(
          "Exact asset ID to download (e.g., 'Rock063' for ambientCG, 'rock_face_04' for Poly Haven). Required for: download"
        ),
        resolution: z.enum(['1K', '2K', '4K', '8K']).optional().describe(
          "Texture resolution. Default: 2K. Used by: download"
        ),
        uv_scale: z.number().optional().describe(
          "UV scale for generated .tmat. Default: 10. Used by: download"
        ),
        limit: z.number().min(1).max(25).optional().describe(
          "Max search results. Default: 10. Used by: search"
        ),
      },
    },
    async ({ action, query, provider, asset_id, resolution, uv_scale, limit }) => {
      try {
        const prov = provider || 'ambientcg'
        const res = resolution || '2K'
        const uv = uv_scale || 10
        const lim = limit || 10

        // ── list_providers ──
        if (action === 'list_providers') {
          return {
            content: [{
              type: 'text' as const,
              text: [
                '# Available Texture Providers',
                '',
                '## ambientcg',
                '- URL: https://ambientcg.com',
                '- License: CC0 (public domain)',
                '- 2000+ PBR materials',
                '- Downloads as ZIP with all PBR maps',
                '- Asset IDs: PascalCase (e.g., Rock063, Ground067)',
                '',
                '## polyhaven',
                '- URL: https://polyhaven.com',
                '- License: CC0 (public domain)',
                '- Large library of photoscanned textures',
                '- Individual file downloads per map',
                '- Asset IDs: snake_case (e.g., rock_face_04, aerial_asphalt_01)',
              ].join('\n'),
            }],
          }
        }

        // ── search ──
        if (action === 'search') {
          if (!query) {
            return {
              content: [{ type: 'text' as const, text: 'Error: query is required for search' }],
              isError: true,
            }
          }

          const results = prov === 'polyhaven'
            ? await polyhavenSearch(query, lim)
            : await ambientcgSearch(query, lim)

          if (results.length === 0) {
            return {
              content: [{ type: 'text' as const, text: `No textures found for "${query}" on ${prov}` }],
            }
          }

          const lines = results.map((r, i) => {
            const parts = [`${i + 1}. **${r.id}** — ${r.name}`]
            if (r.downloadCount) parts.push(`   Downloads: ${r.downloadCount.toLocaleString()}`)
            if (r.categories?.length) parts.push(`   Categories: ${r.categories.join(', ')}`)
            return parts.join('\n')
          })

          return {
            content: [{
              type: 'text' as const,
              text: `Found ${results.length} textures on ${prov} for "${query}":\n\n${lines.join('\n\n')}\n\nUse \`download\` with \`asset_id\` to fetch any of these.`,
            }],
          }
        }

        // ── download ──
        if (action === 'download') {
          if (!asset_id) {
            return {
              content: [{ type: 'text' as const, text: 'Error: asset_id is required for download' }],
              isError: true,
            }
          }

          const projectRoot = resolveProjectRoot()
          const texturesDir = path.join(projectRoot, 'Assets', 'terrain', 'textures', asset_id)

          // Check if already downloaded
          if (fs.existsSync(texturesDir) && fs.readdirSync(texturesDir).length > 0) {
            // Rebuild from existing files
            const existing: DownloadedTexture[] = []
            for (const fname of fs.readdirSync(texturesDir)) {
              const slotMap = prov === 'polyhaven' ? POLYHAVEN_SLOT_MAP : AMBIENTCG_SLOT_MAP
              for (const [suffix, slot] of Object.entries(slotMap)) {
                if (fname.includes(suffix)) {
                  existing.push({
                    slot,
                    assetPath: `terrain/textures/${asset_id}/${fname}`,
                    filename: fname,
                  })
                  break
                }
              }
            }

            const tmatPath = generateTmat(asset_id, existing, projectRoot, uv)
            return {
              content: [{
                type: 'text' as const,
                text: [
                  `Textures already exist for ${asset_id}. Regenerated .tmat.`,
                  '',
                  '## Slot Mapping',
                  ...existing.map(t => `- **${t.slot}**: ${t.assetPath}`),
                  '',
                  `## Material: \`${tmatPath}\``,
                  '',
                  'Drag this .tmat from the Asset Browser onto your Terrain.',
                ].join('\n'),
              }],
            }
          }

          // Download fresh
          const textures = prov === 'polyhaven'
            ? await polyhavenDownload(asset_id, res, projectRoot)
            : await ambientcgDownload(asset_id, res, projectRoot)

          if (textures.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: `Error: No PBR textures found in download for ${asset_id} at ${res}`,
              }],
              isError: true,
            }
          }

          const tmatPath = generateTmat(asset_id, textures, projectRoot, uv)

          return {
            content: [{
              type: 'text' as const,
              text: [
                `Downloaded ${asset_id} (${res}) from ${prov}`,
                '',
                '## Extracted Textures',
                ...textures.map(t => `- **${t.slot}**: ${t.filename}`),
                '',
                `## Generated Material: \`${tmatPath}\``,
                '',
                '## Next Steps',
                '1. Select your Terrain in s&box',
                `2. Drag \`${asset_id.toLowerCase()}.tmat\` from Asset Browser → Materials list`,
                '3. Paint with the Mapping (M) tool',
              ].join('\n'),
            }],
          }
        }

        return {
          content: [{ type: 'text' as const, text: `Unknown action: ${action}` }],
          isError: true,
        }
      } catch (err) {
        return {
          content: [{
            type: 'text' as const,
            text: `Error: ${err instanceof Error ? err.message : String(err)}`,
          }],
          isError: true,
        }
      }
    }
  )
}
