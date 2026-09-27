<template>
  <div class="page-container">
    <h2 class="page-title">OAuth 应用管理</h2>

    <a-card class="card-shadow">
      <a-table
        :columns="columns"
        :data-source="apps"
        :loading="loading"
        :pagination="false"
        row-key="id"
        size="small"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'appName'">
            {{ record.appName }}
            <a-tag v-if="record.isDisabled" color="red" style="margin-left: 4px">已禁用</a-tag>
          </template>
          <template v-if="column.key === 'clientId'">
            <a-typography-text code style="font-size: 12px">{{ record.clientId }}</a-typography-text>
          </template>
          <template v-if="column.key === 'developerUsername'">
            {{ record.developerUsername }}
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
        </template>
      </a-table>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAdminOAuthApps, type OAuthAppAdminItem } from '@/api/oauth2'

const loading = ref(false)
const apps = ref<OAuthAppAdminItem[]>([])

const columns = [
  { title: '应用名称', dataIndex: 'appName', key: 'appName' },
  { title: 'Client ID', key: 'clientId' },
  { title: '开发者', key: 'developerUsername' },
  { title: '创建时间', key: 'createdAt' },
]

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchApps() {
  loading.value = true
  try {
    const res = await getAdminOAuthApps()
    if (res.status === 200) {
      apps.value = res.data
    }
  } finally {
    loading.value = false
  }
}

onMounted(fetchApps)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
