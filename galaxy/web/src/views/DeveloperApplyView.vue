<template>
  <div class="page-container">
    <h2 class="page-title">开发者选项</h2>

    <!-- 已是开发者 -->
    <template v-if="status?.isDeveloper">
      <a-card class="card-shadow">
        <a-result status="success" title="你已是开发者了~" sub-title="你可以创建和管理 OAuth2 应用了 ヾ(≧▽≦*)o">
          <template #extra>
            <router-link to="/developer/apps">
              <a-button type="primary">管理我的应用</a-button>
            </router-link>
          </template>
        </a-result>
      </a-card>
    </template>

    <!-- 审核中 -->
    <template v-else-if="status?.status === 'pending'">
      <a-card class="card-shadow">
        <a-result status="info" title="申请审核中" sub-title="请等待管理员审核您的开发者申请">
          <template #extra>
            <a-descriptions :column="1" bordered size="small">
              <a-descriptions-item label="开发者名称">{{ status.developerName }}</a-descriptions-item>
              <a-descriptions-item label="申请用途">{{ status.purpose }}</a-descriptions-item>
              <a-descriptions-item label="申请时间">{{ formatDate(status.createdAt) }}</a-descriptions-item>
            </a-descriptions>
          </template>
        </a-result>
      </a-card>
    </template>

    <!-- 已拒绝 -->
    <template v-else-if="status?.status === 'rejected'">
      <a-card class="card-shadow">
        <a-result status="warning" title="申请被拒绝" :sub-title="status.reviewNote || '管理员未提供原因'">
          <template #extra>
            <a-button type="primary" @click="showApplyForm = true">重新申请</a-button>
          </template>
        </a-result>
      </a-card>
    </template>

    <!-- 未申请 -->
    <template v-else>
      <a-card class="card-shadow">
        <a-result status="info" title="成为开发者" sub-title="开发者可以创建 OAuth2 应用，便于第三方应用接入">
          <template #extra>
            <a-button type="primary" @click="showApplyForm = true">申请成为开发者</a-button>
          </template>
        </a-result>
      </a-card>
    </template>

    <!-- 申请表单 -->
    <a-modal
      v-model:open="showApplyForm"
      title="申请成为开发者"
      @ok="handleApply"
      :confirm-loading="applying"
      width="560px"
    >
      <a-form layout="vertical">
        <a-form-item label="开发者名称" required>
          <a-input v-model:value="applyForm.developerName" placeholder="请输入开发者名称" />
        </a-form-item>
        <a-form-item label="申请用途说明" required>
          <a-textarea v-model:value="applyForm.purpose" placeholder="简述您的开发目的和计划" :rows="3" />
        </a-form-item>
        <a-form-item label="主页/网站 URL">
          <a-input v-model:value="applyForm.websiteUrl" placeholder="可选，您的主页或 GitHub 地址" />
        </a-form-item>
        <a-form-item label="联系方式">
          <a-input v-model:value="applyForm.contactInfo" placeholder="可选，QQ/微信/Telegram 等" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getDeveloperStatus, applyDeveloper, type DeveloperStatus } from '@/api/developer'

const loading = ref(false)
const applying = ref(false)
const showApplyForm = ref(false)
const status = ref<DeveloperStatus | null>(null)

const applyForm = reactive({
  developerName: '',
  purpose: '',
  websiteUrl: '',
  contactInfo: '',
})

function formatDate(dateStr?: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}

async function fetchStatus() {
  loading.value = true
  try {
    const res = await getDeveloperStatus()
    if (res.status === 200) {
      status.value = res.data
    }
  } finally {
    loading.value = false
  }
}

async function handleApply() {
  if (!applyForm.developerName.trim() || !applyForm.purpose.trim()) {
    message.warning('请填写必填项')
    return
  }
  applying.value = true
  try {
    const res = await applyDeveloper({
      developerName: applyForm.developerName.trim(),
      purpose: applyForm.purpose.trim(),
      websiteUrl: applyForm.websiteUrl.trim() || undefined,
      contactInfo: applyForm.contactInfo.trim() || undefined,
    })
    if (res.status === 200) {
      message.success(res.data || res.message || '申请已提交')
      showApplyForm.value = false
      await fetchStatus()
    } else {
      message.error(res.message || '申请失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '申请失败')
  } finally {
    applying.value = false
  }
}

onMounted(fetchStatus)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
