import api from './auth'

export interface OAuthAppCreateResult {
  id: number
  clientId: string
  clientSecret: string
  appName: string
  redirectUris: string[]
  scopes: string[]
}

export interface OAuthAppListItem {
  id: number
  clientId: string
  appName: string
  redirectUris: string
  scopes: string
  isDisabled: boolean
  createdAt: string
}

export interface OAuthAppAdminItem {
  id: number
  developerUserId: number
  clientId: string
  appName: string
  developerUsername: string
  redirectUris: string
  scopes: string
  isDisabled: boolean
  createdAt: string
}

export interface OAuthAppInfo {
  clientId: string
  appName: string
  scopes: string
  developerName: string
}

export async function createOAuthApp(data: {
  appName: string
  redirectUris: string[]
  scopes: string[]
}) {
  const res = await api.post('/oauth2/apps', data)
  return res.data
}

export async function getOAuthApps() {
  const res = await api.get('/oauth2/apps')
  return res.data
}

export async function updateOAuthApp(id: number, data: {
  appName?: string
  redirectUris?: string[]
  scopes?: string[]
}) {
  const res = await api.put(`/oauth2/apps/${id}`, data)
  return res.data
}

export async function deleteOAuthApp(id: number) {
  const res = await api.delete(`/oauth2/apps/${id}`)
  return res.data
}

export async function getOAuthAppInfo(clientId: string) {
  const res = await api.get('/oauth2/app-info', { params: { client_id: clientId } })
  return res.data
}

export async function authorizeOAuth(data: {
  clientId: string
  redirectUri: string
  scope?: string
  state?: string
  approved: boolean
}) {
  const res = await api.post('/oauth2/authorize', data)
  return res.data
}

export async function getAdminOAuthApps(developerUserId?: number) {
  const params: Record<string, any> = {}
  if (developerUserId !== undefined) params.developerUserId = developerUserId
  const res = await api.get('/admin/oauth-apps', { params })
  return res.data
}

export async function toggleAdminOAuthApp(id: number) {
  const res = await api.put(`/admin/oauth-apps/${id}/toggle`)
  return res.data
}
