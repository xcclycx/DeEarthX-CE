<template>
  <div class="page-container">
    <div class="search-bar">
      <a-input-search
        v-model:value="query"
        placeholder="搜索模组 ID..."
        size="large"
        enter-button="搜索"
        style="max-width: 480px"
        @search="doSearch"
      />
    </div>

    <a-card class="card-shadow">
      <a-table
        :columns="columns"
        :data-source="results"
        :loading="loading"
        :pagination="pagination"
        row-key="modId"
        @change="handleTableChange"
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
          <template v-if="column.key === 'updatedAt'">
            {{ formatDate(record.updatedAt) }}
          </template>
        </template>
      </a-table>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { searchMods, type ModInfo } from '@/api/mod'
import ModTag from '@/components/ModTag.vue'

const route = useRoute()
const router = useRouter()

const query = ref((route.query.q as string) || '')
const loading = ref(false)
const results = ref<ModInfo[]>([])
const total = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)

const pagination = ref({
  current: 1,
  pageSize: 20,
  total: 0,
  showSizeChanger: false,
  showTotal: (t: number) => `共 ${t} 条`,
})

const columns = [
  { title: '模组 ID', dataIndex: 'modId', key: 'modId' },
  { title: '客户端', key: 'clientOk', width: 120 },
  { title: '服务端', key: 'serverOk', width: 120 },
  { title: '提交次数', dataIndex: 'submitCount', key: 'submitCount', width: 100 },
  { title: '更新时间', key: 'updatedAt', width: 180 },
]

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function doSearch() {
  if (!query.value.trim()) return
  router.replace({ query: { q: query.value.trim() } })
  currentPage.value = 1
  await fetchResults()
}

async function fetchResults() {
  loading.value = true
  try {
    const res = await searchMods(query.value.trim(), currentPage.value, pageSize.value)
    if (res.status === 200) {
      results.value = res.data.items
      total.value = res.data.total
      pagination.value.current = res.data.page
      pagination.value.total = res.data.total
    }
  } finally {
    loading.value = false
  }
}

function handleTableChange(pag: { current?: number }) {
  currentPage.value = pag.current ?? 1
  fetchResults()
}

onMounted(() => {
  if (query.value.trim()) {
    fetchResults()
  }
})
</script>

<style scoped>
.search-bar {
  margin-bottom: 20px;
  display: flex;
  justify-content: flex-start;
}
</style>
