<template>
  <div class="page-container">
    <h2 class="page-title">模组审核</h2>

    <a-card class="card-shadow">
      <div class="filter-bar">
        <a-radio-group v-model:value="statusFilter" button-style="solid" @change="fetchMods">
          <a-radio-button :value="0">待审核</a-radio-button>
          <a-radio-button :value="1">已通过</a-radio-button>
          <a-radio-button :value="2">已拒绝</a-radio-button>
          <a-radio-button :value="null">全部</a-radio-button>
        </a-radio-group>
        <span class="pending-hint" v-if="pendingCount > 0">{{ pendingCount }} 条待审核</span>
      </div>

      <a-table
        :columns="columns"
        :data-source="mods"
        :loading="loading"
        :pagination="pagination"
        row-key="id"
        @change="handleTableChange"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'modId'">
            <router-link :to="`/mod/${record.modId}`">{{ record.modId }}</router-link>
          </template>
          <template v-if="column.key === 'status'">
            <a-tag :color="statusColor(record.status)">
              {{ statusText(record.status) }}
            </a-tag>
          </template>
          <template v-if="column.key === 'clientOk'">
            <ModTag :ok="record.clientOk" />
          </template>
          <template v-if="column.key === 'serverOk'">
            <ModTag :ok="record.serverOk" />
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
          <template v-if="column.key === 'actions'">
            <a-space>
              <template v-if="record.status === 0">
                <a-button type="primary" size="small" @click="openReviewModal(record, 1)">通过</a-button>
                <a-button danger size="small" @click="openReviewModal(record, 2)">拒绝</a-button>
              </template>
              <template v-else>
                <a-button size="small" @click="openEditModal(record)">编辑</a-button>
                <a-button v-if="record.status === 1" danger size="small" @click="openReviewModal(record, 2)">撤回</a-button>
                <a-button v-if="record.status === 2" type="primary" size="small" @click="openReviewModal(record, 1)">重新通过</a-button>
              </template>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- 审核/重新审核弹窗 -->
    <a-modal
      v-model:open="reviewModalVisible"
      :title="reviewAction === 1 ? '审核通过' : '审核拒绝'"
      @ok="handleReview"
      :confirm-loading="saving"
    >
      <a-form layout="vertical">
        <a-form-item label="模组 ID">
          <a-input :value="reviewTarget?.modId" disabled />
        </a-form-item>
        <a-form-item label="客户端合规" v-if="reviewAction === 1">
          <a-switch v-model:checked="reviewForm.clientOk" />
        </a-form-item>
        <a-form-item label="服务端合规" v-if="reviewAction === 1">
          <a-switch v-model:checked="reviewForm.serverOk" />
        </a-form-item>
        <a-form-item label="备注" v-if="reviewAction === 1">
          <a-textarea v-model:value="reviewForm.reviewNote" :rows="2" placeholder="审核备注（可选）" />
        </a-form-item>
        <a-form-item label="拒绝原因" v-if="reviewAction === 2">
          <a-textarea v-model:value="reviewForm.reviewNote" :rows="3" placeholder="请填写拒绝原因" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 编辑弹窗（已审核的 mod） -->
    <a-modal
      v-model:open="editModalVisible"
      title="编辑模组"
      @ok="handleEdit"
      :confirm-loading="saving"
    >
      <a-form layout="vertical">
        <a-form-item label="模组 ID">
          <a-input :value="editTarget?.modId" disabled />
        </a-form-item>
        <a-form-item label="客户端合规">
          <a-switch v-model:checked="editForm.clientOk" />
        </a-form-item>
        <a-form-item label="服务端合规">
          <a-switch v-model:checked="editForm.serverOk" />
        </a-form-item>
        <a-form-item label="备注">
          <a-textarea v-model:value="editForm.note" :rows="3" placeholder="备注信息" />
        </a-form-item>
        <a-form-item label="审核备注">
          <a-textarea v-model:value="editForm.reviewNote" :rows="2" placeholder="审核备注" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getAdminMods, reviewAdminMod, type AdminMod } from '@/api/admin'
import api from '@/api/auth'
import ModTag from '@/components/ModTag.vue'

const loading = ref(false)
const saving = ref(false)
const mods = ref<AdminMod[]>([])
const currentPage = ref(1)
const pageSize = ref(50)
const total = ref(0)
const statusFilter = ref<number | null>(0)
const pendingCount = ref(0)

// 审核弹窗
const reviewModalVisible = ref(false)
const reviewAction = ref(1)
const reviewTarget = ref<AdminMod | null>(null)
const reviewForm = reactive({
  clientOk: false,
  serverOk: false,
  reviewNote: '',
})

// 编辑弹窗
const editModalVisible = ref(false)
const editTarget = ref<AdminMod | null>(null)
const editForm = reactive({
  clientOk: false,
  serverOk: false,
  note: '',
  reviewNote: '',
})

const pagination = ref({
  current: 1,
  pageSize: 50,
  total: 0,
  showSizeChanger: false,
  showTotal: (t: number) => `共 ${t} 条`,
})

const columns = [
  { title: '模组 ID', dataIndex: 'modId', key: 'modId' },
  { title: '状态', key: 'status', width: 100 },
  { title: '客户端', key: 'clientOk', width: 90 },
  { title: '服务端', key: 'serverOk', width: 90 },
  { title: '提交次数', dataIndex: 'submitCount', key: 'submitCount', width: 80 },
  { title: '审核备注', dataIndex: 'reviewNote', key: 'reviewNote', ellipsis: true },
  { title: '提交时间', key: 'createdAt', width: 160 },
  { title: '操作', key: 'actions', width: 200 },
]

function statusColor(status: number) {
  if (status === 0) return 'orange'
  if (status === 1) return 'green'
  return 'red'
}

function statusText(status: number) {
  if (status === 0) return '待审核'
  if (status === 1) return '已通过'
  return '已拒绝'
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchMods() {
  loading.value = true
  try {
    const res = await getAdminMods(currentPage.value, pageSize.value, statusFilter.value ?? undefined)
    if (res.status === 200) {
      mods.value = res.data.items ?? res.data
      total.value = res.data.total ?? mods.value.length
      pagination.value.current = currentPage.value
      pagination.value.total = total.value
    }
  } finally {
    loading.value = false
  }
}

async function fetchPendingCount() {
  try {
    const res = await getAdminMods(1, 1, 0)
    if (res.status === 200) {
      pendingCount.value = res.data.total ?? 0
    }
  } catch { /* ignore */ }
}

function handleTableChange(pag: { current?: number }) {
  currentPage.value = pag.current ?? 1
  fetchMods()
}

function openReviewModal(record: AdminMod, action: number) {
  reviewTarget.value = record
  reviewAction.value = action
  reviewForm.clientOk = record.clientOk
  reviewForm.serverOk = record.serverOk
  reviewForm.reviewNote = ''
  reviewModalVisible.value = true
}

function openEditModal(record: AdminMod) {
  editTarget.value = record
  editForm.clientOk = record.clientOk
  editForm.serverOk = record.serverOk
  editForm.note = record.note ?? ''
  editForm.reviewNote = record.reviewNote ?? ''
  editModalVisible.value = true
}

async function handleReview() {
  if (!reviewTarget.value) return
  if (reviewAction.value === 2 && !reviewForm.reviewNote.trim()) {
    message.warning('请填写拒绝原因')
    return
  }

  saving.value = true
  try {
    // 如果是通过，先更新合规标记
    if (reviewAction.value === 1) {
      await api.put(`/admin/mods/${reviewTarget.value.id}`, {
        clientOk: reviewForm.clientOk,
        serverOk: reviewForm.serverOk,
      })
    }

    await reviewAdminMod(reviewTarget.value.id, reviewAction.value, reviewForm.reviewNote || undefined)
    message.success(reviewAction.value === 1 ? '审核通过' : '已拒绝')
    reviewModalVisible.value = false
    await fetchMods()
    await fetchPendingCount()
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  } finally {
    saving.value = false
  }
}

async function handleEdit() {
  if (!editTarget.value) return
  saving.value = true
  try {
    await api.put(`/admin/mods/${editTarget.value.id}`, {
      clientOk: editForm.clientOk,
      serverOk: editForm.serverOk,
      note: editForm.note,
      reviewNote: editForm.reviewNote,
    })
    message.success('已更新')
    editModalVisible.value = false
    await fetchMods()
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  fetchMods()
  fetchPendingCount()
})
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}

.filter-bar {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 16px;
}

.pending-hint {
  color: var(--status-warning-default);
  font-size: 14px;
  font-weight: 500;
}

.text-tertiary {
  color: var(--text-tertiary);
  font-size: 13px;
}
</style>
