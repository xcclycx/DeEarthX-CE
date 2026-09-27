<template>
  <div class="page-container">
    <h2 class="page-title">模组管理</h2>

    <a-card class="card-shadow">
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
              <a-button type="link" size="small" @click="openEditModal(record)">编辑</a-button>
              <a-popconfirm title="确定要删除此模组吗？" @confirm="handleDelete(record.id)">
                <a-button type="link" danger size="small">删除</a-button>
              </a-popconfirm>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <a-modal
      v-model:open="editModalVisible"
      title="编辑模组"
      @ok="handleEdit"
      :confirm-loading="saving"
    >
      <a-form layout="vertical">
        <a-form-item label="客户端合规">
          <a-switch v-model:checked="editForm.clientOk" />
        </a-form-item>
        <a-form-item label="服务端合规">
          <a-switch v-model:checked="editForm.serverOk" />
        </a-form-item>
        <a-form-item label="备注">
          <a-textarea v-model:value="editForm.note" :rows="3" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getAdminMods, updateAdminMod, deleteAdminMod, type AdminMod } from '@/api/admin'
import ModTag from '@/components/ModTag.vue'

const loading = ref(false)
const saving = ref(false)
const mods = ref<AdminMod[]>([])
const currentPage = ref(1)
const pageSize = ref(50)
const total = ref(0)

const editModalVisible = ref(false)
const editForm = reactive({
  id: 0,
  clientOk: false,
  serverOk: false,
  note: '',
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
  { title: '客户端', key: 'clientOk', width: 110 },
  { title: '服务端', key: 'serverOk', width: 110 },
  { title: '提交次数', dataIndex: 'submitCount', key: 'submitCount', width: 90 },
  { title: '创建时间', key: 'createdAt', width: 170 },
  { title: '操作', key: 'actions', width: 140 },
]

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchMods() {
  loading.value = true
  try {
    const res = await getAdminMods(currentPage.value, pageSize.value)
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

function handleTableChange(pag: { current?: number }) {
  currentPage.value = pag.current ?? 1
  fetchMods()
}

function openEditModal(record: AdminMod) {
  editForm.id = record.id
  editForm.clientOk = record.clientOk
  editForm.serverOk = record.serverOk
  editForm.note = record.note || ''
  editModalVisible.value = true
}

async function handleEdit() {
  saving.value = true
  try {
    await updateAdminMod(editForm.id, {
      clientOk: editForm.clientOk,
      serverOk: editForm.serverOk,
      note: editForm.note,
    })
    message.success('更新成功')
    editModalVisible.value = false
    await fetchMods()
  } catch (err: any) {
    message.error(err.response?.data?.data || '更新失败')
  } finally {
    saving.value = false
  }
}

async function handleDelete(id: number) {
  try {
    await deleteAdminMod(id)
    message.success('删除成功')
    await fetchMods()
  } catch (err: any) {
    message.error(err.response?.data?.data || '删除失败')
  }
}

onMounted(fetchMods)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
