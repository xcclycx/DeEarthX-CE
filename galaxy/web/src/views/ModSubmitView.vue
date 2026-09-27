<template>
  <div class="page-container">
    <h2 class="page-title">模组提交</h2>

    <a-card class="card-shadow">
      <a-form layout="vertical" style="max-width: 600px">
        <a-form-item label="模组 ID" required>
          <a-input v-model:value="form.modid" placeholder="输入模组ID，多个用逗号分隔" />
        </a-form-item>
        <a-form-item label="类型" required>
          <a-radio-group v-model:value="form.type">
            <a-radio-button value="client">客户端</a-radio-button>
            <a-radio-button value="server">服务端</a-radio-button>
          </a-radio-group>
        </a-form-item>
        <a-form-item>
          <a-button type="primary" :loading="submitting" @click="handleSubmit">提交</a-button>
        </a-form-item>
      </a-form>

      <a-table
        v-if="results.length > 0"
        :columns="resultColumns"
        :data-source="results"
        :pagination="false"
        row-key="modId"
        size="small"
        style="margin-top: 16px"
      />
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { message } from 'ant-design-vue'
import { submitMod } from '@/api/admin'

const submitting = ref(false)
const results = ref<any[]>([])

const form = reactive({
  modid: '',
  type: 'client' as 'client' | 'server',
})

const resultColumns = [
  { title: '模组ID', dataIndex: 'modId', key: 'modId' },
  { title: '状态', key: 'status' },
  { title: '提交次数', dataIndex: 'submitCount', key: 'submitCount' },
]

async function handleSubmit() {
  if (!form.modid.trim()) {
    message.warning('请输入模组ID')
    return
  }
  submitting.value = true
  results.value = []
  try {
    const res = await submitMod(form.modid.trim(), form.type)
    if (res.status === 200) {
      results.value = Array.isArray(res.data) ? res.data : [res.data]
      message.success('提交成功')
    } else {
      message.error(res.message || '提交失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '提交失败')
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
