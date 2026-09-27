import api from './auth'

export interface ModInfo {
  id: number
  modId: string
  clientOk: boolean
  serverOk: boolean
  submitCount: number
  note: string
  createdAt: string
  updatedAt: string
}

export interface ModSearchResult {
  items: ModInfo[]
  total: number
  page: number
  pageSize: number
}

export interface ModStats {
  totalMods: number
  clientOk: number
  serverOk: number
  bothOk: number
}

export async function submitMod(type: string, modid: string) {
  const res = await api.post(`/mod/submit/${type}`, { modid })
  return res.data
}

export async function getMod(modId: string) {
  const res = await api.get(`/mod/${modId}`)
  return res.data
}

export async function searchMods(q: string, page = 1, pageSize = 20) {
  const res = await api.get('/mod/search', { params: { q, page, pageSize } })
  return res.data
}

export async function getModStats() {
  const res = await api.get('/mod/stats')
  return res.data
}
