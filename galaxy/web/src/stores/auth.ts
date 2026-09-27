import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as loginApi, register as registerApi, getMe, type UserInfo } from '@/api/auth'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem('token'))
  const user = ref<UserInfo | null>(null)
  const initialized = ref(false)

  const isLoggedIn = computed(() => !!token.value)
  const permissions = computed(() => user.value?.permissions ?? [])
  const isDeveloper = computed(() => user.value?.isDeveloper ?? false)

  function hasPermission(perm: string): boolean {
    return permissions.value.includes(perm)
  }

  function hasAnyPermission(perms: string[]): boolean {
    return perms.some((p) => permissions.value.includes(p))
  }

  async function login(username: string, password: string) {
    const res = await loginApi({ username, password })
    if (res.status === 200) {
      token.value = res.data
      localStorage.setItem('token', res.data)
      await fetchUser()
    }
    return res
  }

  async function register(username: string, email: string, password: string, verifyCode?: string) {
    const res = await registerApi({ username, email, password, verifyCode })
    if (res.status === 200) {
      token.value = res.data
      localStorage.setItem('token', res.data)
      await fetchUser()
    }
    return res
  }

  async function fetchUser() {
    if (!token.value) return
    try {
      const res = await getMe()
      if (res.status === 200) {
        const raw = res.data
        let perms: string[] = []
        if (typeof raw.permissions === 'string') {
          try { perms = JSON.parse(raw.permissions) } catch { perms = [] }
        } else if (Array.isArray(raw.permissions)) {
          perms = raw.permissions
        }
        user.value = { ...raw, permissions: perms }
      }
    } catch {
      logout()
    } finally {
      initialized.value = true
    }
  }

  async function init() {
    if (token.value && !user.value) {
      await fetchUser()
    } else {
      initialized.value = true
    }
  }

  function logout() {
    token.value = null
    user.value = null
    localStorage.removeItem('token')
  }

  return {
    token,
    user,
    initialized,
    isLoggedIn,
    permissions,
    isDeveloper,
    hasPermission,
    hasAnyPermission,
    login,
    register,
    fetchUser,
    init,
    logout,
  }
})
