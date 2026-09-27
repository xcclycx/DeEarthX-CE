import api from './auth'

export interface AdminUser {
  id: number
  username: string
  email: string
  permissions: string[]
  isDisabled: boolean
  isDeveloper: boolean
  createdAt: string
}

export interface AdminMod {
  id: number
  modId: string
  clientOk: boolean
  serverOk: boolean
  submitCount: number
  note: string
  status: number
  reviewNote: string
  createdAt: string
  updatedAt: string
}

export interface AdminSettings {
  [key: string]: string
}

export async function getAdminUsers() {
  const res = await api.get('/admin/users')
  return res.data
}

export async function updateUserPermissions(id: number, permissions: string[]) {
  const res = await api.put(`/admin/users/${id}/permissions`, { permissions })
  return res.data
}

export async function toggleUser(id: number) {
  const res = await api.put(`/admin/users/${id}/toggle`)
  return res.data
}

export async function createUser(username: string, password: string, email?: string, permissions?: string[]) {
  const res = await api.post('/admin/users', { username, password, email, permissions })
  return res.data
}

export async function updateUser(id: number, data: { email?: string; password?: string; permissions?: string[] }) {
  const res = await api.put(`/admin/users/${id}`, data)
  return res.data
}

export async function deleteUser(id: number) {
  const res = await api.delete(`/admin/users/${id}`)
  return res.data
}

export async function getAdminMods(page = 1, pageSize = 50, status?: number) {
  const params: Record<string, any> = { page, pageSize }
  if (status !== undefined) params.status = status
  const res = await api.get('/admin/mods', { params })
  return res.data
}

export async function reviewAdminMod(id: number, status: number, reviewNote?: string) {
  const res = await api.post(`/admin/mods/${id}/review`, { status, reviewNote })
  return res.data
}

export async function updateAdminMod(id: number, data: { clientOk: boolean; serverOk: boolean; note: string }) {
  const res = await api.put(`/admin/mods/${id}`, data)
  return res.data
}

export async function deleteAdminMod(id: number) {
  const res = await api.delete(`/admin/mods/${id}`)
  return res.data
}

export async function getAdminSettings() {
  const res = await api.get('/admin/settings')
  return res.data
}

export async function updateAdminSettings(settings: AdminSettings) {
  const res = await api.put('/admin/settings', settings)
  return res.data
}

export async function submitMod(modid: string, type: 'client' | 'server') {
  const res = await api.post(`/mod/submit/${type}`, { modid })
  return res.data
}
