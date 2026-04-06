import Fuse from 'fuse.js'
import type { SboxApiData, SboxType, FlatMember } from '../types.js'

export interface SearchIndexes {
  types: SboxType[]
  typeFuse: Fuse<SboxType>
  typeLookup: Map<string, SboxType>
  members: FlatMember[]
  memberFuse: Fuse<FlatMember>
  namespaceMap: Map<string, SboxType[]>
  docFuse: Fuse<{ name: string; fullName: string; kind: string; summary: string; parentType?: string }>
}

function buildSignature(method: { Name: string; ReturnType: string; Parameters?: { Name: string; Type: string; Default?: string }[]; IsStatic?: boolean }): string {
  const params = (method.Parameters || [])
    .map(p => {
      const def = p.Default ? ` = ${p.Default}` : ''
      return `${p.Name}: ${p.Type}${def}`
    })
    .join(', ')
  const stat = method.IsStatic ? ' [static]' : ''
  return `${method.Name}(${params}) → ${method.ReturnType}${stat}`
}

function flattenMembers(types: SboxType[]): FlatMember[] {
  const members: FlatMember[] = []

  for (const type of types) {
    if (type.Methods) {
      for (const m of type.Methods) {
        members.push({
          name: m.Name,
          fullName: m.FullName,
          kind: 'method',
          declaringType: type.FullName,
          returnType: m.ReturnType,
          signature: buildSignature(m),
          summary: m.Documentation?.Summary,
          isStatic: m.IsStatic,
        })
      }
    }
    if (type.Properties) {
      for (const p of type.Properties) {
        members.push({
          name: p.Name,
          fullName: p.FullName,
          kind: 'property',
          declaringType: type.FullName,
          returnType: p.PropertyType,
          summary: p.Documentation?.Summary,
        })
      }
    }
    if (type.Fields) {
      for (const f of type.Fields) {
        members.push({
          name: f.Name,
          fullName: f.FullName,
          kind: 'field',
          declaringType: type.FullName,
          returnType: f.FieldType,
          summary: f.Documentation?.Summary,
          isStatic: f.IsStatic,
        })
      }
    }
  }

  return members
}

export function buildIndexes(data: SboxApiData): SearchIndexes {
  const types = data.Types
  console.error(`[arenula-api] Building indexes for ${types.length} types...`)

  // Type fuzzy search
  const typeFuse = new Fuse(types, {
    keys: [
      { name: 'Name', weight: 3 },
      { name: 'FullName', weight: 1 },
      { name: 'Documentation.Summary', weight: 0.5 },
    ],
    threshold: 0.4,
    includeScore: true,
    minMatchCharLength: 2,
  })

  // Type lookup by FullName (case-insensitive)
  const typeLookup = new Map<string, SboxType>()
  for (const t of types) {
    typeLookup.set(t.FullName.toLowerCase(), t)
    // Also allow lookup by short Name if it's unique enough
    const existing = typeLookup.get(t.Name.toLowerCase())
    if (!existing) {
      typeLookup.set(t.Name.toLowerCase(), t)
    }
  }

  // Namespace grouping
  const namespaceMap = new Map<string, SboxType[]>()
  for (const t of types) {
    const ns = t.Namespace || '(global)'
    const list = namespaceMap.get(ns) || []
    list.push(t)
    namespaceMap.set(ns, list)
  }

  // Flatten all members
  const members = flattenMembers(types)
  console.error(`[arenula-api] Indexed ${members.length} members`)

  const memberFuse = new Fuse(members, {
    keys: [
      { name: 'name', weight: 3 },
      { name: 'fullName', weight: 1 },
      { name: 'summary', weight: 0.5 },
    ],
    threshold: 0.4,
    includeScore: true,
    minMatchCharLength: 2,
  })

  // Documentation full-text index
  const docEntries: { name: string; fullName: string; kind: string; summary: string; parentType?: string }[] = []

  for (const t of types) {
    if (t.Documentation?.Summary) {
      docEntries.push({
        name: t.Name,
        fullName: t.FullName,
        kind: t.Group,
        summary: t.Documentation.Summary,
      })
    }
    for (const m of t.Methods || []) {
      if (m.Documentation?.Summary) {
        docEntries.push({
          name: m.Name,
          fullName: m.FullName,
          kind: 'method',
          summary: m.Documentation.Summary,
          parentType: t.FullName,
        })
      }
    }
    for (const p of t.Properties || []) {
      if (p.Documentation?.Summary) {
        docEntries.push({
          name: p.Name,
          fullName: p.FullName,
          kind: 'property',
          summary: p.Documentation.Summary,
          parentType: t.FullName,
        })
      }
    }
  }

  const docFuse = new Fuse(docEntries, {
    keys: [{ name: 'summary', weight: 1 }],
    threshold: 0.5,
    includeScore: true,
    minMatchCharLength: 3,
  })

  console.error(`[arenula-api] Doc index: ${docEntries.length} entries`)
  console.error('[arenula-api] Indexes ready')

  return { types, typeFuse, typeLookup, members, memberFuse, namespaceMap, docFuse }
}
