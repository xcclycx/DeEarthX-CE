<template>
  <div class="page-container">
    <h2 class="page-title">系统设置</h2>

    <a-card class="card-shadow" :loading="loading">
      <a-form layout="vertical" style="max-width: 600px">
        <a-divider orientation="right"><span style="font-weight:700;background:linear-gradient(135deg,#000000);-webkit-background-clip:text;-webkit-text-fill-color:transparent;">注册设置</span></a-divider>

        <a-form-item label="开放注册">
          <a-switch
            :checked="settings.registration_open === 'true'"
            @change="(val: boolean) => settings.registration_open = val ? 'true' : 'false'"
          />
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px">
            关闭后新用户将无法自行注册
          </div>
        </a-form-item>

        <a-form-item label="默认权限（新注册用户）">
          <a-checkbox-group v-model:value="defaultPermissions" :options="permissionOptions" />
        </a-form-item>

        <a-divider orientation="right"><span style="font-weight:700;background:linear-gradient(135deg,#000000);-webkit-background-clip:text;-webkit-text-fill-color:transparent;">SMTP 邮箱服务</span></a-divider>

        <a-form-item label="启用 SMTP">
          <a-switch
            :checked="settings.smtp_enabled === 'true'"
            @change="(val: boolean) => settings.smtp_enabled = val ? 'true' : 'false'"
          />
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px">
            启用后注册需要邮箱验证码
          </div>
        </a-form-item>

        <template v-if="settings.smtp_enabled === 'true'">
          <a-form-item label="SMTP 主机">
            <a-input v-model:value="settings.smtp_host" placeholder="smtp.example.com" />
          </a-form-item>
          <a-form-item label="SMTP 端口">
            <a-input v-model:value="settings.smtp_port" placeholder="587" />
          </a-form-item>
          <a-form-item label="SMTP 用户名">
            <a-input v-model:value="settings.smtp_username" placeholder="user@example.com" />
          </a-form-item>
          <a-form-item label="SMTP 密码">
            <a-input-password v-model:value="settings.smtp_password" placeholder="留空则不修改" />
          </a-form-item>
          <a-form-item label="发件人地址">
            <a-input v-model:value="settings.smtp_from" placeholder="noreply@example.com" />
          </a-form-item>
        </template>

        <a-divider orientation="right"><span style="font-weight:700;background:linear-gradient(135deg,#000000);-webkit-background-clip:text;-webkit-text-fill-color:transparent;">开发者设置</span></a-divider>
        
        <a-form-item label="开发者申请需要审核">
          <a-switch
            :checked="settings.developer_require_approval === 'true'"
            @change="(val: boolean) => settings.developer_require_approval = val ? 'true' : 'false'"
          />
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px">
            关闭后用户申请开发者时自动通过
          </div>
        </a-form-item>

        <a-form-item>
          <a-button type="primary" :loading="saving" @click="handleSave">保存设置</a-button>
        </a-form-item>
      </a-form>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getAdminSettings, updateAdminSettings, type AdminSettings } from '@/api/admin'

const loading = ref(false)
const saving = ref(false)
const defaultPermissions = ref<string[]>([])

const permissionOptions = [
  { label: '提交模组', value: 'mod.submit' },
  { label: '查询模组', value: 'mod.query' },
  { label: '管理API KEY', value: 'apikey.manage' },
  { label: '申请开发者', value: 'developer.apply' },
]

const settings = reactive<AdminSettings>({
  registration_open: 'true',
  smtp_host: '',
  smtp_port: '587',
  smtp_username: '',
  smtp_password: '',
  smtp_from: '',
  smtp_enabled: 'false',
  default_permissions: '[]',
  developer_require_approval: 'true',
})

async function fetchSettings() {
  loading.value = true
  try {
    const res = await getAdminSettings()
    if (res.status === 200) {
      Object.assign(settings, res.data)
      // 解析默认权限
      try {
        defaultPermissions.value = JSON.parse(settings.default_permissions || '[]')
      } catch {
        defaultPermissions.value = []
      }
    }
  } finally {
    loading.value = false
  }
}

async function handleSave() {
  saving.value = true
  try {
    const toSave = { ...settings }
    // 序列化默认权限
    toSave.default_permissions = JSON.stringify(defaultPermissions.value)
    // 如果密码为空则不发送
    if (!toSave.smtp_password) delete toSave.smtp_password
    await updateAdminSettings(toSave)
    message.success('设置已保存')
    // 重新获取以刷新加密后的密码显示
    await fetchSettings()
  } catch (err: any) {
    message.error(err.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(fetchSettings)
</script>

<style scoped>
.page-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-default);
  margin-bottom: 24px;
}
</style>
