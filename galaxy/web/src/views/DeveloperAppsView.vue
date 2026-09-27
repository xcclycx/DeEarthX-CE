<template>
  <div class="page-container">
    <h2 class="page-title">我的 OAuth 应用</h2>

    <a-card class="card-shadow">
      <template #extra>
        <a-button type="primary" @click="showCreateModal = true">创建应用</a-button>
      </template>

      <!-- 新创建的应用密钥提示 -->
      <a-alert
        v-if="newlyCreatedSecret"
        type="success"
        show-icon
        closable
        style="margin-bottom: 16px"
        message="OAuth 应用已创建"
      >
        <template #description>
          <div>
            <p style="color: var(--status-error-default); font-weight: 600">请妥善保存你的 Client Secret，关闭后将无法再次查看哦~</p>
            <div style="margin-top: 8px">
              <p><strong>Client ID:</strong> <a-typography-text code>{{ newlyCreatedSecret.clientId }}</a-typography-text></p>
              <p><strong>Client Secret:</strong> <a-typography-text code>{{ newlyCreatedSecret.clientSecret }}</a-typography-text></p>
              <a-button size="small" @click="copyText(`你的Client ID:${newlyCreatedSecret!.clientId} 你的Client Secret:${newlyCreatedSecret!.clientSecret}`)">复制凭据</a-button>
            </div>
          </div>
        </template>
      </a-alert>

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
          <template v-if="column.key === 'redirectUris'">
            <div v-for="(uri, i) in parseJson(record.redirectUris)" :key="i" style="font-size: 12px">{{ uri }}</div>
          </template>
          <template v-if="column.key === 'scopes'">
            <a-tag v-for="s in parseJson(record.scopes)" :key="s" style="margin: 2px">{{ s }}</a-tag>
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
          <template v-if="column.key === 'actions'">
            <a-button type="link" size="small" @click="openEditModal(record)">编辑</a-button>
            <a-popconfirm title="确定要删除此应用吗？" @confirm="handleDelete(record.id)">
              <a-button type="link" danger size="small">删除</a-button>
            </a-popconfirm>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- 创建应用弹窗 -->
    <a-modal
      v-model:open="showCreateModal"
      title="创建 OAuth 应用"
      @ok="handleCreate"
      :confirm-loading="creating"
      width="560px"
    >
      <a-form layout="vertical">
        <a-form-item label="应用名称" required>
          <a-input v-model:value="createForm.appName" placeholder="请输入应用名称" />
        </a-form-item>
        <a-form-item label="回调地址" required>
          <div v-for="(uri, i) in createForm.redirectUris" :key="i" style="display: flex; gap: 8px; margin-bottom: 8px">
            <a-input v-model:value="createForm.redirectUris[i]" placeholder="https://example.com/callback" style="flex: 1" />
            <a-button danger @click="createForm.redirectUris.splice(i, 1)" :disabled="createForm.redirectUris.length <= 1">删除</a-button>
          </div>
          <a-button type="dashed" @click="createForm.redirectUris.push('')">添加回调地址</a-button>
        </a-form-item>
        <a-form-item label="申请的 Scope">
          <a-checkbox-group v-model:value="createForm.scopes" :options="scopeOptions" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 编辑应用弹窗 -->
    <a-modal
      v-model:open="showEditModal"
      title="编辑 OAuth 应用"
      @ok="handleUpdate"
      :confirm-loading="updating"
      width="560px"
    >
      <a-form layout="vertical">
        <a-form-item label="应用名称">
          <a-input v-model:value="editForm.appName" />
        </a-form-item>
        <a-form-item label="回调地址">
          <div v-for="(uri, i) in editForm.redirectUris" :key="i" style="display: flex; gap: 8px; margin-bottom: 8px">
            <a-input v-model:value="editForm.redirectUris[i]" style="flex: 1" />
            <a-button danger @click="editForm.redirectUris.splice(i, 1)" :disabled="editForm.redirectUris.length <= 1">删除</a-button>
          </div>
          <a-button type="dashed" @click="editForm.redirectUris.push('')">添加回调地址</a-button>
        </a-form-item>
        <a-form-item label="申请的 Scope">
          <a-checkbox-group v-model:value="editForm.scopes" :options="scopeOptions" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { createOAuthApp, getOAuthApps, updateOAuthApp, deleteOAuthApp, type OAuthAppListItem } from '@/api/oauth2'

const scopeOptions = [
  { label: '读取用户信息 (user:read)', value: 'user:read' },
  { label: '查询模组 (mod:read)', value: 'mod:read' },
  { label: '提交模组 (mod:submit)', value: 'mod.submit' },
  { label: '查询模组 (mod.query)', value: 'mod.query' },
  { label: '管理模组 (mod.manage)', value: 'mod.manage' },
  { label: '管理用户 (user.manage)', value: 'user.manage' },
  { label: '系统设置 (system.settings)', value: 'system.settings' },
  { label: '管理API KEY (apikey.manage)', value: 'apikey.manage' },
  { label: '管理OAuth2 (oauth2.manage)', value: 'oauth2.manage' },
  { label: '申请开发者 (developer.apply)', value: 'developer.apply' },
]

const loading = ref(false)
const creating = ref(false)
const updating = ref(false)
const apps = ref<OAuthAppListItem[]>([])
const showCreateModal = ref(false)
const showEditModal = ref(false)
const editingId = ref(0)
const newlyCreatedSecret = ref<{ clientId: string; clientSecret: string } | null>(null)

const createForm = reactive({
  appName: '',
  redirectUris: [''],
  scopes: [] as string[],
})

const editForm = reactive({
  appName: '',
  redirectUris: [] as string[],
  scopes: [] as string[],
})

const columns = [
  { title: '应用名称', key: 'appName' },
  { title: 'Client ID', key: 'clientId' },
  { title: '回调地址', key: 'redirectUris' },
  { title: 'Scope', key: 'scopes' },
  { title: '创建时间', key: 'createdAt' },
  { title: '操作', key: 'actions', width: 140 },
]

function parseJson(str: string): any[] {
  try { return JSON.parse(str) } catch { return [] }
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchApps() {
  loading.value = true
  try {
    const res = await getOAuthApps()
    if (res.status === 200) {
      apps.value = res.data
    }
  } finally {
    loading.value = false
  }
}

async function handleCreate() {
  if (!createForm.appName.trim()) {
    message.warning('请输入应用名称')
    return
  }
  const uris = createForm.redirectUris.filter(u => u.trim())
  if (uris.length === 0) {
    message.warning('至少需要一个回调地址')
    return
  }
  creating.value = true
  try {
    const res = await createOAuthApp({
      appName: createForm.appName.trim(),
      redirectUris: uris,
      scopes: createForm.scopes,
    })
    if (res.status === 200) {
      newlyCreatedSecret.value = {
        clientId: res.data.clientId,
        clientSecret: res.data.clientSecret,
      }
      message.success('应用创建成功')
      showCreateModal.value = false
      createForm.appName = ''
      createForm.redirectUris = ['']
      createForm.scopes = []
      await fetchApps()
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '创建失败')
  } finally {
    creating.value = false
  }
}

function openEditModal(record: OAuthAppListItem) {
  editingId.value = record.id
  editForm.appName = record.appName
  editForm.redirectUris = parseJson(record.redirectUris)
  editForm.scopes = parseJson(record.scopes)
  showEditModal.value = true
}

async function handleUpdate() {
  updating.value = true
  try {
    await updateOAuthApp(editingId.value, {
      appName: editForm.appName.trim() || undefined,
      redirectUris: editForm.redirectUris.filter(u => u.trim()),
      scopes: editForm.scopes,
    })
    message.success('应用已更新')
    showEditModal.value = false
    await fetchApps()
  } catch (err: any) {
    message.error(err.response?.data?.message || '更新失败')
  } finally {
    updating.value = false
  }
}

async function handleDelete(id: number) {
  try {
    await deleteOAuthApp(id)
    message.success('应用已删除')
    await fetchApps()
  } catch (err: any) {
    message.error(err.response?.data?.message || '删除失败')
  }
}

function copyText(text: string) {
  navigator.clipboard.writeText(text).then(() => {
    message.success('已复制到剪贴板')
  }).catch(() => {
    message.error('复制失败')
  })
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
