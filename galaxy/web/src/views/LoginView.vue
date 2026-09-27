<template>
  <div class="auth-page">
    <div class="auth-card card-shadow">
      <h1 class="auth-title brand-gradient-text">Galaxy</h1>
      <p class="auth-subtitle">登录到你的账户</p>

      <a-form
        :model="form"
        :rules="rules"
        @finish="handleLogin"
        layout="vertical"
      >
        <a-form-item name="username" label="用户名">
          <a-input v-model:value="form.username" placeholder="请输入用户名" size="large" />
        </a-form-item>
        <a-form-item name="password" label="密码">
          <a-input-password v-model:value="form.password" placeholder="请输入密码" size="large" />
        </a-form-item>
        <a-form-item>
          <a-button type="primary" html-type="submit" :loading="loading" block size="large">
            登录
          </a-button>
        </a-form-item>
      </a-form>

      <div class="auth-footer">
        还没有账户？<router-link to="/register">立即注册</router-link>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { message } from 'ant-design-vue'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const loading = ref(false)

const form = reactive({
  username: '',
  password: '',
})

const rules = {
  username: [{ required: true, message: '请输入用户名' }],
  password: [{ required: true, message: '请输入密码' }],
}

async function handleLogin() {
  loading.value = true
  try {
    const res = await auth.login(form.username, form.password)
    if (res.status === 200) {
      message.success('登录成功')
      const redirect = (route.query.redirect as string) || '/'
      router.push(redirect)
    } else {
      message.error(res.message || '登录失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.data || '登录失败')
  } finally {
    loading.value = false
  }
}
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
