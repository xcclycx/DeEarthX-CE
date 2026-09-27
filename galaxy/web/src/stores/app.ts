import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ModStats } from '@/api/mod'
import { getModStats } from '@/api/mod'

export const useAppStore = defineStore('app', () => {
  const stats = ref<ModStats | null>(null)
  const loading = ref(false)

  async function fetchStats() {
    loading.value = true
    try {
      const res = await getModStats()
      if (res.status === 200) {
        stats.value = res.data
      }
    } finally {
      loading.value = false
    }
  }

  return {
    stats,
    loading,
    fetchStats,
  }
})
