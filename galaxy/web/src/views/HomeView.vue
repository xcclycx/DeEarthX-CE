<template>
  <div class="home-page">
    <div class="hero-section">
      <h1 class="hero-title brand-gradient-text">Galaxy</h1>
      <p class="hero-subtitle" style="color: var(--text-tertiary)">模组合规性查询平台</p>
    </div>

    <div class="search-section">
      <a-input-search
        v-model:value="searchQuery"
        placeholder="搜索模组 ID..."
        size="large"
        enter-button="搜索"
        style="max-width: 560px"
        @search="handleSearch"
      />
    </div>

    <div class="stats-section">
      <a-row :gutter="[16, 16]">
        <a-col :xs="24" :sm="8">
          <a-card class="stat-card card-shadow">
            <a-statistic title="总模组数" :value="stats?.totalMods ?? 0" :loading="appStore.loading">
              <template #prefix><DatabaseOutlined /></template>
            </a-statistic>
          </a-card>
        </a-col>
        <a-col :xs="24" :sm="8">
          <a-card class="stat-card card-shadow">
            <a-statistic title="客户端合规" :value="stats?.clientOk ?? 0" :loading="appStore.loading">
              <template #prefix><CheckCircleOutlined style="color: var(--status-success-default)" /></template>
            </a-statistic>
          </a-card>
        </a-col>
        <a-col :xs="24" :sm="8">
          <a-card class="stat-card card-shadow">
            <a-statistic title="服务端合规" :value="stats?.serverOk ?? 0" :loading="appStore.loading">
              <template #prefix><CheckCircleOutlined style="color: var(--status-success-default)" /></template>
            </a-statistic>
          </a-card>
        </a-col>
      </a-row>
    </div>

    <div class="recent-section">
      <a-card title="最近提交的模组" class="card-shadow">
        <a-table
          :columns="recentColumns"
          :data-source="recentMods"
          :loading="recentLoading"
          :pagination="false"
          size="small"
          row-key="modId"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'modId'">
              <router-link :to="`/mod/${record.modId}`">{{ record.modId }}</router-link>
            </template>
            <template v-if="column.key === 'clientOk'">
              <ModTag :ok="record.clientOk" />
            </template>
            <template v-if="column.key === 'serverOk'">
              <ModTag :ok="record.serverOk" />
            </template>
          </template>
        </a-table>
      </a-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { DatabaseOutlined, CheckCircleOutlined } from '@ant-design/icons-vue'
import { useAppStore } from '@/stores/app'
import { searchMods, type ModInfo } from '@/api/mod'
import ModTag from '@/components/ModTag.vue'

const router = useRouter()
const appStore = useAppStore()
const stats = ref(appStore.stats)

const searchQuery = ref('')
const recentLoading = ref(false)
const recentMods = ref<ModInfo[]>([])

const recentColumns = [
  { title: '模组 ID', dataIndex: 'modId', key: 'modId' },
  { title: '客户端', key: 'clientOk' },
  { title: '服务端', key: 'serverOk' },
  { title: '提交次数', dataIndex: 'submitCount', key: 'submitCount' },
]

function handleSearch() {
  if (searchQuery.value.trim()) {
    router.push({ name: 'search', query: { q: searchQuery.value.trim() } })
  }
}

onMounted(async () => {
  await appStore.fetchStats()
  stats.value = appStore.stats
  recentLoading.value = true
  try {
    const res = await searchMods('', 1, 10)
    if (res.status === 200) {
      recentMods.value = res.data.items
    }
  } finally {
    recentLoading.value = false
  }
})
</script>

<style scoped>
.home-page {
  max-width: 960px;
  margin: 0 auto;
  padding: 40px 24px;
}

.hero-section {
  text-align: center;
  margin-bottom: 32px;
}

.hero-title {
  font-size: 48px;
  font-weight: 800;
  margin-bottom: 8px;
}

.hero-subtitle {
  font-size: 16px;
}

.search-section {
  display: flex;
  justify-content: center;
  margin-bottom: 40px;
}

.stats-section {
  margin-bottom: 32px;
}

.stat-card {
  text-align: center;
}

.recent-section {
  margin-top: 8px;
}
</style>
