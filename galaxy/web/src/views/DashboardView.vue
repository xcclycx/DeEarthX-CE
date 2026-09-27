<template>
  <div class="page-container">
    <h2 class="page-title">控制台</h2>

    <a-card title="API 密钥管理" class="card-shadow" style="margin-bottom: 24px">
      <template #extra>
        <a-button type="primary" @click="showCreateModal = true">创建密钥</a-button>
      </template>

      <a-alert
        v-if="newlyCreatedKey"
        type="success"
        show-icon
        closable
        style="margin-bottom: 16px"
        message="API 密钥已创建"
      >
        <template #description>
          <div>
            <p style="color: var(--status-error-default); font-weight: 600">请立即保存此密钥，关闭后将无法再次查看完整密钥！</p>
            <div style="display: flex; align-items: center; gap: 8px; margin-top: 8px">
              <a-typography-text code style="font-size: 14px">{{ newlyCreatedKey }}</a-typography-text>
              <a-button size="small" @click="copyKey(newlyCreatedKey!)">复制</a-button>
            </div>
          </div>
        </template>
      </a-alert>

      <a-table
        :columns="columns"
        :data-source="apiKeys"
        :loading="loading"
        :pagination="false"
        row-key="id"
        size="small"
        :row-class-name="(record: ApiKey) => record.isSystem ? 'system-key-row' : ''"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'name'">
            {{ record.name }}
            <a-tag v-if="record.isSystem" color="blue" style="margin-left: 4px">系统</a-tag>
          </template>
          <template v-if="column.key === 'prefix'">
            <a-typography-text code>{{ record.prefix }}••••••••</a-typography-text>
          </template>
          <template v-if="column.key === 'permissions'">
            <template v-if="record.isSystem">
              <a-tag color="blue">等同于用户权限</a-tag>
            </template>
            <template v-else>
              <a-tag v-for="p in parsePermissions(record.permissions)" :key="p" style="margin: 2px">{{ permLabel(p) }}</a-tag>
              <a-button v-if="!record.isSystem" type="link" size="small" @click="openEditPermissions(record)">编辑</a-button>
            </template>
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
          <template v-if="column.key === 'lastUsed'">
            {{ record.lastUsed ? formatDate(record.lastUsed) : '从未使用' }}
          </template>
          <template v-if="column.key === 'actions'">
            <template v-if="!record.isSystem">
              <a-popconfirm title="确定要撤销此密钥吗？" @confirm="handleDelete(record.id)">
                <a-button type="link" danger size="small">撤销</a-button>
              </a-popconfirm>
            </template>
            <span v-else style="color: var(--text-tertiary); font-size: 12px">不可操作</span>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- 创建 API KEY 弹窗 -->
    <a-modal
      v-model:open="showCreateModal"
      title="创建 API 密钥"
      @ok="handleCreate"
      :confirm-loading="creating"
    >
      <a-form layout="vertical">
        <a-form-item label="密钥名称">
          <a-input v-model:value="newKeyName" placeholder="例如：我的应用" />
        </a-form-item>
        <a-form-item label="权限配置">
          <a-checkbox-group v-model:value="newKeyPermissions" :options="permissionOptions" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 编辑权限弹窗 -->
    <a-modal
      v-model:open="showEditModal"
      title="编辑 API KEY 权限"
      @ok="handleUpdatePermissions"
      :confirm-loading="updatingPermissions"
    >
      <a-checkbox-group v-model:value="editingPermissions" :options="permissionOptions" style="width: 100%" />
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { createApiKey, getApiKeys, deleteApiKey, updateApiKeyPermissions, type ApiKey } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const creating = ref(false)
const updatingPermissions = ref(false)
const apiKeys = ref<ApiKey[]>([])
const newKeyName = ref('')
const newKeyPermissions = ref<string[]>([])
const showCreateModal = ref(false)
const showEditModal = ref(false)
const newlyCreatedKey = ref<string | null>(null)
const editingKeyId = ref<number>(0)
const editingPermissions = ref<string[]>([])

const columns = [
  { title: '名称', key: 'name' },
  { title: '前缀', key: 'prefix' },
  { title: '权限', key: 'permissions' },
  { title: '上次使用', key: 'lastUsed' },
  { title: '创建时间', key: 'createdAt' },
  { title: '操作', key: 'actions', width: 100 },
]

const permissionOptions = computed(() => {
  return auth.permissions.map(p => ({ label: permLabel(p), value: p }))
})

function permLabel(p: string): string {
  const map: Record<string, string> = {
    'mod.submit': '提交模组',
    'mod.query': '查询模组',
    'mod.manage': '管理模组',
    'user.manage': '管理用户',
    'system.settings': '系统设置',
    'apikey.manage': '管理API KEY',
    'oauth2.manage': '管理OAuth2',
    'developer.apply': '申请开发者',
  }
  return map[p] || p
}

function parsePermissions(perms: string[] | string): string[] {
  if (Array.isArray(perms)) return perms
  try { return JSON.parse(perms) } catch { return [] }
}

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchKeys() {
  loading.value = true
  try {
    const res = await getApiKeys()
    if (res.status === 200) {
      apiKeys.value = res.data
    }
  } finally {
    loading.value = false
  }
}

async function handleCreate() {
  if (!newKeyName.value.trim()) {
    message.warning('请输入密钥名称')
    return
  }
  creating.value = true
  try {
    const res = await createApiKey({ name: newKeyName.value.trim(), permissions: newKeyPermissions.value })
    if (res.status === 200) {
      newlyCreatedKey.value = res.data.key
      message.success('密钥创建成功')
      showCreateModal.value = false
      newKeyName.value = ''
      newKeyPermissions.value = []
      await fetchKeys()
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '创建失败')
  } finally {
    creating.value = false
  }
}

async function handleDelete(id: number) {
  try {
    await deleteApiKey(id)
    message.success('密钥已撤销')
    await fetchKeys()
  } catch (err: any) {
    message.error(err.response?.data?.message || '撤销失败')
  }
}

function openEditPermissions(record: ApiKey) {
  editingKeyId.value = record.id
  editingPermissions.value = parsePermissions(record.permissions)
  showEditModal.value = true
}

async function handleUpdatePermissions() {
  updatingPermissions.value = true
  try {
    await updateApiKeyPermissions(editingKeyId.value, editingPermissions.value)
    message.success('权限已更新')
    showEditModal.value = false
    await fetchKeys()
  } catch (err: any) {
    message.error(err.response?.data?.message || '更新失败')
  } finally {
    updatingPermissions.value = false
  }
}

function copyKey(key: string) {
  navigator.clipboard.writeText(key).then(() => {
    message.success('已复制到剪贴板')
  }).catch(() => {
    message.error('复制失败')
  })
}

onMounted(fetchKeys)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}

:deep(.system-key-row) {
  background: var(--bg-base-secondary) !important;
  opacity: 0.75;
}
</style>
