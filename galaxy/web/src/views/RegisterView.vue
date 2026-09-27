<template>
  <div class="auth-page">
    <div class="auth-card card-shadow">
      <h1 class="auth-title brand-gradient-text">Galaxy</h1>

      <!-- 注册已关闭 -->
      <template v-if="registrationClosed">
        <a-result status="warning" title="注册已关闭" sub-title="管理员已关闭注册功能，请联系管理员获取账户">
          <template #extra>
            <router-link to="/login">
              <a-button type="primary">返回登录</a-button>
            </router-link>
          </template>
        </a-result>
      </template>

      <!-- 正常注册表单 -->
      <template v-else>
        <p class="auth-subtitle">创建新账户</p>
        <a-form
          :model="form"
          :rules="rules"
          @finish="handleRegister"
          layout="vertical"
        >
          <a-form-item name="username" label="用户名">
            <a-input v-model:value="form.username" placeholder="请输入用户名" size="large" />
          </a-form-item>
          <a-form-item name="email" label="邮箱">
            <a-input v-model:value="form.email" placeholder="请输入邮箱" size="large" />
          </a-form-item>
          <a-form-item name="password" label="密码">
            <a-input-password v-model:value="form.password" placeholder="请输入密码" size="large" />
          </a-form-item>
          <!-- SMTP 启用时显示确认密码和验证码 -->
          <template v-if="smtpEnabled">
            <a-form-item name="confirmPassword" label="确认密码">
              <a-input-password v-model:value="form.confirmPassword" placeholder="请再次输入密码" size="large" />
            </a-form-item>
            <a-form-item name="verifyCode" label="邮箱验证码">
              <div style="display: flex; gap: 8px">
                <a-input v-model:value="form.verifyCode" placeholder="6位验证码" size="large" style="flex: 1" />
                <a-button
                  size="large"
                  :disabled="codeCooldown > 0 || !form.email"
                  :loading="sendingCode"
                  @click="handleSendCode"
                >
                  {{ codeCooldown > 0 ? `${codeCooldown}s` : '发送验证码' }}
                </a-button>
              </div>
            </a-form-item>
          </template>
          <a-form-item>
            <a-button type="primary" html-type="submit" :loading="loading" block size="large">
              注册
            </a-button>
          </a-form-item>
        </a-form>

        <div class="auth-footer">
          已有账户？<router-link to="/login">立即登录</router-link>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { message } from 'ant-design-vue'
import { useAuthStore } from '@/stores/auth'
import { getAuthSettings, sendVerifyCode } from '@/api/auth'

const router = useRouter()
const auth = useAuthStore()
const loading = ref(false)
const sendingCode = ref(false)
const registrationClosed = ref(false)
const smtpEnabled = ref(false)
const codeCooldown = ref(0)
let cooldownTimer: ReturnType<typeof setInterval> | null = null

const form = reactive({
  username: '',
  email: '',
  password: '',
  confirmPassword: '',
  verifyCode: '',
})

const validateConfirmPassword = async (_rule: any, value: string) => {
  if (smtpEnabled.value && value !== form.password) {
    throw new Error('两次输入的密码不一致')
  }
}

const rules = {
  username: [{ required: true, message: '请输入用户名' }],
  email: [
    { required: true, message: '请输入邮箱' },
    { type: 'email' as const, message: '邮箱格式不正确' },
  ],
  password: [
    { required: true, message: '请输入密码' },
    { min: 6, message: '密码至少 6 位' },
  ],
  confirmPassword: smtpEnabled.value ? [{ required: true, message: '请确认密码' }, { validator: validateConfirmPassword }] : [],
  verifyCode: smtpEnabled.value ? [{ required: true, message: '请输入验证码' }] : [],
}

async function checkSettings() {
  try {
    const res = await getAuthSettings()
    if (res.status === 200) {
      if (res.data.registration_open === 'false') {
        registrationClosed.value = true
      }
      smtpEnabled.value = res.data.smtp_enabled === 'true'
    }
  } catch {
    // 获取设置失败时不阻止注册
  }
}

async function handleSendCode() {
  if (!form.email) {
    message.warning('请先输入邮箱')
    return
  }
  sendingCode.value = true
  try {
    const res = await sendVerifyCode(form.email)
    if (res.status === 200) {
      message.success('验证码已发送')
      codeCooldown.value = 60
      cooldownTimer = setInterval(() => {
        codeCooldown.value--
        if (codeCooldown.value <= 0 && cooldownTimer) {
          clearInterval(cooldownTimer)
          cooldownTimer = null
        }
      }, 1000)
    } else {
      message.error(res.message || '发送失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '发送失败')
  } finally {
    sendingCode.value = false
  }
}

async function handleRegister() {
  if (smtpEnabled.value && form.confirmPassword !== form.password) {
    message.error('两次输入的密码不一致')
    return
  }
  loading.value = true
  try {
    const res = await auth.register(
      form.username,
      form.email,
      form.password,
      smtpEnabled.value ? form.verifyCode : undefined,
    )
    if (res.status === 200) {
      message.success('注册成功')
      router.push('/')
    } else {
      message.error(res.message || '注册失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '注册失败')
  } finally {
    loading.value = false
  }
}

onMounted(checkSettings)
</script>

<style scoped>
.auth-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-base-secondary);
}

.auth-card {
  width: 400px;
  padding: 40px 32px;
  background: var(--bg-base-default);
  border-radius: var(--radius-lg);
}

.auth-title {
  font-size: 32px;
  font-weight: 800;
  text-align: center;
  margin-bottom: 4px;
}

.auth-subtitle {
  text-align: center;
  color: var(--text-tertiary);
  margin-bottom: 32px;
}

.auth-footer {
  text-align: center;
  color: var(--text-tertiary);
  font-size: 14px;
}
</style>
