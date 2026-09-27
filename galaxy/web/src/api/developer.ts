import api from './auth'

export interface DeveloperStatus {
  isDeveloper: boolean
  status: 'none' | 'pending' | 'approved' | 'rejected'
  developerName?: string
  purpose?: string
  websiteUrl?: string
  contactInfo?: string
  reviewNote?: string
  createdAt?: string
}

export interface DeveloperApplication {
  id: number
  userId: number
  username: string
  developerName: string
  purpose: string
  websiteUrl?: string
  contactInfo?: string
  status: string
  reviewNote?: string
  reviewedAt?: string
  createdAt: string
}

export async function applyDeveloper(data: {
  developerName: string
  purpose: string
  websiteUrl?: string
  contactInfo?: string
}) {
  const res = await api.post('/developer/apply', data)
  return res.data
}

export async function getDeveloperStatus() {
  const res = await api.get('/developer/status')
  return res.data
}

export async function getAdminDevelopers() {
  const res = await api.get('/admin/developers')
  return res.data
}

export async function reviewDeveloper(id: number, approved: boolean, reviewNote?: string) {
  const res = await api.put(`/admin/developers/${id}/review`, { approved, reviewNote })
  return res.data
}
