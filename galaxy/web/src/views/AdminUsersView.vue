<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">用户管理</h2>
      <a-button type="primary" @click="openCreateModal">创建用户</a-button>
    </div>

    <a-card class="card-shadow">
      <a-table
        :columns="columns"
        :data-source="users"
        :loading="loading"
        :pagination="false"
        row-key="id"
        size="middle"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'permissions'">
            <a-space :size="4" wrap>
              <PermissionTag v-for="p in parsePermissions(record.permissions)" :key="p" :permission="p" />
            </a-space>
          </template>
          <template v-if="column.key === 'status'">
            <a-tag :color="record.isDisabled ? 'error' : 'success'">
              {{ record.isDisabled ? '已禁用' : '正常' }}
            </a-tag>
          </template>
          <template v-if="column.key === 'createdAt'">
            {{ formatDate(record.createdAt) }}
          </template>
          <template v-if="column.key === 'actions'">
            <a-space>
              <a-button type="link" size="small" @click="openEditModal(record)">编辑</a-button>
              <a-button type="link" size="small" @click="openPermModal(record)">权限</a-button>
              <a-popconfirm
                :title="record.isDisabled ? '确定要启用此用户吗？' : '确定要禁用此用户吗？'"
                @confirm="handleToggle(record)"
              >
                <a-button type="link" :danger="!record.isDisabled" size="small">
                  {{ record.isDisabled ? '启用' : '禁用' }}
                </a-button>
              </a-popconfirm>
              <a-popconfirm title="确定要删除此用户吗？此操作不可恢复" @confirm="handleDelete(record)">
                <a-button type="link" danger size="small">删除</a-button>
              </a-popconfirm>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- 创建/编辑用户弹窗 -->
    <a-modal
      v-model:open="userModalVisible"
      :title="isCreate ? '创建用户' : '编辑用户'"
      @ok="handleSaveUser"
      :confirm-loading="saving"
    >
      <a-form layout="vertical">
        <a-form-item v-if="isCreate" label="用户名">
          <a-input v-model:value="userForm.username" placeholder="至少3个字符" />
        </a-form-item>
        <a-form-item v-if="isCreate" label="用户名" style="display:none">
          <a-input v-model:value="userForm.username" />
        </a-form-item>
        <a-form-item label="邮箱">
          <a-input v-model:value="userForm.email" placeholder="可选" />
        </a-form-item>
        <a-form-item :label="isCreate ? '密码' : '新密码'">
          <a-input-password v-model:value="userForm.password" :placeholder="isCreate ? '至少6个字符' : '留空则不修改'" />
        </a-form-item>
        <a-form-item v-if="isCreate" label="权限">
          <a-checkbox-group v-model:value="userForm.permissions" style="width: 100%">
            <a-row :gutter="[8, 12]">
              <a-col :span="12" v-for="p in allPermissions" :key="p">
                <a-checkbox :value="p">
                  <PermissionTag :permission="p" />
                </a-checkbox>
              </a-col>
            </a-row>
          </a-checkbox-group>
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 权限编辑弹窗 -->
    <a-modal
      v-model:open="permModalVisible"
      title="编辑权限"
      @ok="handleUpdatePermissions"
      :confirm-loading="saving"
    >
      <p style="color: var(--text-tertiary); margin-bottom: 16px">
        为用户 <strong>{{ editUser?.username }}</strong> 分配权限
      </p>
      <a-checkbox-group v-model:value="editPermissions" style="width: 100%">
        <a-row :gutter="[8, 12]">
          <a-col :span="12" v-for="p in allPermissions" :key="p">
            <a-checkbox :value="p">
              <PermissionTag :permission="p" />
            </a-checkbox>
          </a-col>
        </a-row>
      </a-checkbox-group>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import {
  getAdminUsers,
  updateUserPermissions,
  toggleUser,
  createUser,
  updateUser,
  deleteUser,
  type AdminUser,
} from '@/api/admin'
import PermissionTag from '@/components/PermissionTag.vue'

const loading = ref(false)
const saving = ref(false)
const users = ref<AdminUser[]>([])

const permModalVisible = ref(false)
const editUser = ref<AdminUser | null>(null)
const editPermissions = ref<string[]>([])

const userModalVisible = ref(false)
const isCreate = ref(true)
const editingUserId = ref<number | null>(null)
const userForm = reactive({
  username: '',
  email: '',
  password: '',
  permissions: [] as string[],
})

const allPermissions = [
  'mod.submit',
  'mod.query',
  'mod.manage',
  'user.manage',
  'apikey.manage',
  'system.settings',
]

const columns = [
  { title: '用户名', dataIndex: 'username', key: 'username' },
  { title: '邮箱', dataIndex: 'email', key: 'email' },
  { title: '权限', key: 'permissions' },
  { title: '状态', key: 'status', width: 90 },
  { title: '注册时间', key: 'createdAt', width: 170 },
  { title: '操作', key: 'actions', width: 220 },
]

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

function parsePermissions(perm: string[] | string): string[] {
  if (Array.isArray(perm)) return perm
  try { return JSON.parse(perm) } catch { return [] }
}

async function fetchUsers() {
  loading.value = true
  try {
    const res = await getAdminUsers()
    if (res.status === 200) {
      users.value = res.data
    }
  } finally {
    loading.value = false
  }
}

function openCreateModal() {
  isCreate.value = true
  editingUserId.value = null
  userForm.username = ''
  userForm.email = ''
  userForm.password = ''
  userForm.permissions = [...allPermissions.slice(0, 3)] // 默认权限
  userModalVisible.value = true
}

function openEditModal(record: AdminUser) {
  isCreate.value = false
  editingUserId.value = record.id
  userForm.username = record.username
  userForm.email = record.email
  userForm.password = ''
  userForm.permissions = []
  userModalVisible.value = true
}

function openPermModal(record: AdminUser) {
  editUser.value = record
  editPermissions.value = parsePermissions(record.permissions)
  permModalVisible.value = true
}

async function handleSaveUser() {
  saving.value = true
  try {
    if (isCreate.value) {
      if (!userForm.username || userForm.username.length < 3) {
        message.warning('用户名至少3个字符')
        return
      }
      if (!userForm.password || userForm.password.length < 6) {
        message.warning('密码至少6个字符')
        return
      }
      await createUser(userForm.username, userForm.password, userForm.email || undefined, userForm.permissions)
      message.success('用户已创建')
    } else {
      const data: Record<string, any> = {}
      if (userForm.email) data.email = userForm.email
      if (userForm.password) data.password = userForm.password
      await updateUser(editingUserId.value!, data)
      message.success('用户已更新')
    }
    userModalVisible.value = false
    await fetchUsers()
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  } finally {
    saving.value = false
  }
}

async function handleUpdatePermissions() {
  if (!editUser.value) return
  saving.value = true
  try {
    await updateUserPermissions(editUser.value.id, editPermissions.value)
    message.success('权限更新成功')
    permModalVisible.value = false
    await fetchUsers()
  } catch (err: any) {
    message.error(err.response?.data?.message || '更新失败')
  } finally {
    saving.value = false
  }
}

async function handleToggle(record: AdminUser) {
  try {
    await toggleUser(record.id)
    message.success(record.isDisabled ? '已启用' : '已禁用')
    await fetchUsers()
  } catch (err: any) {
    message.error(err.response?.data?.message || '操作失败')
  }
}

async function handleDelete(record: AdminUser) {
  try {
    await deleteUser(record.id)
    message.success('用户已删除')
    await fetchUsers()
  } catch (err: any) {
    message.error(err.response?.data?.message || '删除失败')
  }
}

onMounted(fetchUsers)
</script>

<style scoped>
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 24px;
}

.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin: 0;
}
</style>
