<template>
  <div class="page-container">
    <a-spin :spinning="loading">
      <a-card v-if="modInfo" class="card-shadow">
        <div class="detail-header">
          <h1 class="mod-title">{{ modInfo.modId }}</h1>
        </div>

        <a-descriptions :column="{ xs: 1, sm: 2 }" bordered size="middle">
          <a-descriptions-item label="客户端状态">
            <ModTag :ok="modInfo.clientOk" />
          </a-descriptions-item>
          <a-descriptions-item label="服务端状态">
            <ModTag :ok="modInfo.serverOk" />
          </a-descriptions-item>
          <a-descriptions-item label="提交次数">
            {{ modInfo.submitCount }}
          </a-descriptions-item>
          <a-descriptions-item label="备注">
            {{ modInfo.note || '-' }}
          </a-descriptions-item>
          <a-descriptions-item label="创建时间">
            {{ formatDate(modInfo.createdAt) }}
          </a-descriptions-item>
          <a-descriptions-item label="更新时间">
            {{ formatDate(modInfo.updatedAt) }}
          </a-descriptions-item>
        </a-descriptions>
      </a-card>

      <a-result v-if="notFound" status="404" title="未找到模组" sub-title="该模组不存在或尚未被提交">
        <template #extra>
          <a-button type="primary" @click="$router.push('/')">返回首页</a-button>
        </template>
      </a-result>
    </a-spin>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getMod, type ModInfo } from '@/api/mod'
import ModTag from '@/components/ModTag.vue'

const route = useRoute()
const loading = ref(false)
const notFound = ref(false)
const modInfo = ref<ModInfo | null>(null)

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

onMounted(async () => {
  const modId = route.params.modId as string
  loading.value = true
  try {
    const res = await getMod(modId)
    if (res.status === 200) {
      modInfo.value = res.data
    } else {
      notFound.value = true
    }
  } catch {
    notFound.value = true
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.detail-header {
  margin-bottom: 24px;
}

.mod-title {
  font-size: 28px;
  font-weight: 700;
  color: var(--text-default);
  margin: 0;
}
</style>
