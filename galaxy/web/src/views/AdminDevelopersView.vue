<template>
  <div class="page-container">
    <h2 class="page-title">开发者管理</h2>

    <a-card class="card-shadow">
      <a-table
        :columns="columns"
        :data-source="applications"
        :loading="loading"
        :pagination="false"
        row-key="id"
        size="small"
        :expandedRowKeys="expandedKeys"
        @expand="onExpand"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'status'">
            <a-tag :color="statusColor(record.status)">{{ statusText(record.status) }}</a-tag>
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
          <template v-if="column.key === 'actions'">
            <template v-if="record.status === 'pending'">
              <a-button type="link" size="small" @click="openReviewModal(record, true)">通过</a-button>
              <a-button type="link" danger size="small" @click="openReviewModal(record, false)">拒绝</a-button>
            </template>
            <span v-else style="color: var(--text-tertiary)">已处理</span>
          </template>
        </template>

        <template #expandedRowRender="{ record }">
          <div style="padding: 8px 0">
            <h4 style="margin-bottom: 12px; color: var(--text-secondary)">OAuth 应用</h4>
            <a-spin :spinning="appsLoading[record.userId]">
              <a-table
                v-if="developerApps[record.userId]?.length"
                :columns="appColumns"
                :data-source="developerApps[record.userId]"
                :pagination="false"
                row-key="id"
                size="small"
              >
                <template #bodyCell="{ column, record: app }">
                  <template v-if="column.key === 'appName'">
                    {{ app.appName }}
                    <a-tag v-if="app.isDisabled" color="red" style="margin-left: 4px">已禁用</a-tag>
                  </template>
                  <template v-if="column.key === 'clientId'">
                    <a-typography-text code style="font-size: 12px">{{ app.clientId }}</a-typography-text>
                  </template>
                  <template v-if="column.key === 'createdAt'">
                    {{ formatDate(app.createdAt) }}
                  </template>
                  <template v-if="column.key === 'actions'">
                    <a-popconfirm :title="app.isDisabled ? '确定要启用此应用吗？' : '确定要禁用此应用吗？'" @confirm="handleToggleApp(app)">
                      <a-button type="link" :danger="!app.isDisabled" size="small">
                        {{ app.isDisabled ? '启用' : '禁用' }}
                      </a-button>
                    </a-popconfirm>
                  </template>
                </template>
              </a-table>
              <a-empty v-else description="暂无应用" :image="null" style="padding: 8px 0" />
            </a-spin>
          </div>
        </template>
      </a-table>
    </a-card>

    <!-- 审核弹窗 -->
    <a-modal
      v-model:open="showReviewModal"
      :title="reviewApproved ? '通过申请' : '拒绝申请'"
      @ok="handleReview"
      :confirm-loading="reviewing"
    >
      <a-descriptions :column="1" bordered size="small" style="margin-bottom: 16px">
        <a-descriptions-item label="用户名">{{ reviewingApp?.username }}</a-descriptions-item>
        <a-descriptions-item label="开发者名称">{{ reviewingApp?.developerName }}</a-descriptions-item>
        <a-descriptions-item label="申请用途">{{ reviewingApp?.purpose }}</a-descriptions-item>
        <a-descriptions-item v-if="reviewingApp?.websiteUrl" label="网站">{{ reviewingApp.websiteUrl }}</a-descriptions-item>
        <a-descriptions-item v-if="reviewingApp?.contactInfo" label="联系方式">{{ reviewingApp.contactInfo }}</a-descriptions-item>
      </a-descriptions>
      <a-form layout="vertical">
        <a-form-item label="审核备注">
          <a-textarea v-model:value="reviewNote" placeholder="可选" :rows="2" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getAdminDevelopers, reviewDeveloper, type DeveloperApplication } from '@/api/developer'
import { getAdminOAuthApps, toggleAdminOAuthApp, type OAuthAppAdminItem } from '@/api/oauth2'

const loading = ref(false)
const reviewing = ref(false)
const showReviewModal = ref(false)
const reviewApproved = ref(false)
const reviewNote = ref('')
const reviewingApp = ref<DeveloperApplication | null>(null)
const applications = ref<DeveloperApplication[]>([])
const developerApps = reactive<Record<number, OAuthAppAdminItem[]>>({})
const appsLoading = reactive<Record<number, boolean>>({})
const expandedKeys = ref<number[]>([])

const columns = [
  { title: '用户名', dataIndex: 'username', key: 'username' },
  { title: '开发者名称', dataIndex: 'developerName', key: 'developerName' },
  { title: '申请用途', dataIndex: 'purpose', key: 'purpose', ellipsis: true },
  { title: '状态', key: 'status' },
  { title: '申请时间', key: 'createdAt' },
  { title: '操作', key: 'actions', width: 140 },
]

const appColumns = [
  { title: '应用名称', key: 'appName' },
  { title: 'Client ID', key: 'clientId' },
  { title: '创建时间', key: 'createdAt' },
  { title: '操作', key: 'actions', width: 80 },
]

function statusColor(s: string) {
  return s === 'pending' ? 'orange' : s === 'approved' ? 'green' : 'red'
}

function statusText(s: string) {
  return s === 'pending' ? '待审核' : s === 'approved' ? '已通过' : '已拒绝'
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function onExpand(expanded: boolean, record: DeveloperApplication) {
  if (expanded) {
    expandedKeys.value = [record.id]
    await fetchDeveloperApps(record.userId)
  } else {
    expandedKeys.value = expandedKeys.value.filter(k => k !== record.id)
  }
}

async function fetchDeveloperApps(userId: number) {
  appsLoading[userId] = true
  try {
    const res = await getAdminOAuthApps(userId)
    if (res.status === 200) {
      developerApps[userId] = res.data
    }
  } finally {
    appsLoading[userId] = false
  }
}

function openReviewModal(app: DeveloperApplication, approved: boolean) {
  reviewingApp.value = app
  reviewApproved.value = approved
  reviewNote.value = ''
  showReviewModal.value = true
}

async function handleReview() {
  if (!reviewingApp.value) return
  reviewing.value = true
  try {
    await reviewDeveloper(reviewingApp.value.id, reviewApproved.value, reviewNote.value || undefined)
    message.success(reviewApproved.value ? '已通过' : '已拒绝')
    showReviewModal.value = false
    await fetchApplications()
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  } finally {
    reviewing.value = false
  }
}

async function handleToggleApp(app: OAuthAppAdminItem) {
  try {
    await toggleAdminOAuthApp(app.id)
    message.success(app.isDisabled ? '应用已启用' : '应用已禁用')
    await fetchDeveloperApps(app.developerUserId)
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  }
}

async function fetchApplications() {
  loading.value = true
  try {
    const res = await getAdminDevelopers()
    if (res.status === 200) {
      applications.value = res.data
    }
  } finally {
    loading.value = false
  }
}

onMounted(fetchApplications)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
