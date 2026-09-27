import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  timeout: 10000,
})

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token')
      window.location.href = '/login'
    }
    return Promise.reject(error)
  },
)

export interface LoginParams {
  username: string
  password: string
}

export interface RegisterParams {
  username: string
  email: string
  password: string
  verifyCode?: string
}

export interface UserInfo {
  id: number
  username: string
  permissions: string[]
  isDeveloper: boolean
}

export interface ApiKey {
  id: number
  key?: string
  prefix: string
  name: string
  permissions: string[]
  isSystem: boolean
  lastUsed?: string
  createdAt: string
}

export interface CreateApiKeyParams {
  name: string
  permissions?: string[]
}

export interface AuthSettings {
  registration_open: string
  smtp_enabled: string
  developer_require_approval: string
}

export async function login(params: LoginParams) {
  const res = await api.post('/auth/login', params)
  return res.data
}

export async function register(params: RegisterParams) {
  const res = await api.post('/auth/register', params)
  return res.data
}

export async function getMe() {
  const res = await api.get('/auth/me')
  return res.data
}

export async function getAuthSettings() {
  const res = await api.get('/auth/settings')
  return res.data
}

export async function sendVerifyCode(email: string) {
  const res = await api.post('/auth/send-verify-code', { email })
  return res.data
}

export async function createApiKey(params: CreateApiKeyParams) {
  const res = await api.post('/auth/api-key', params)
  return res.data
}

export async function getApiKeys() {
  const res = await api.get('/auth/api-key')
  return res.data
}

export async function updateApiKeyPermissions(id: number, permissions: string[]) {
  const res = await api.put(`/auth/api-key/${id}/permissions`, { permissions })
  return res.data
}

export async function deleteApiKey(id: number) {
  const res = await api.delete(`/auth/api-key/${id}`)
  return res.data
}

export default api
