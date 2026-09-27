<template>
  <div class="auth-page">
    <div class="auth-card card-shadow" style="width: 480px">
      <h2 class="auth-title" style="font-size: 24px">OAuth 授权</h2>

      <a-spin :spinning="loading">
        <template v-if="appInfo">
          <p style="text-align: center; color: var(--text-tertiary); margin-bottom: 24px">
            应用 <strong>{{ appInfo.appName }}</strong>（开发者：{{ appInfo.developerName }}）请求以下权限：
          </p>

          <div style="margin-bottom: 24px">
            <a-tag v-for="s in parseScopes(appInfo.scopes)" :key="s" color="blue" style="margin: 4px; font-size: 14px; padding: 4px 12px">
              {{ scopeLabel(s) }}
            </a-tag>
          </div>

          <div style="display: flex; gap: 12px; justify-content: center">
            <a-button size="large" @click="handleDeny">拒绝</a-button>
            <a-button type="primary" size="large" @click="handleApprove" :loading="approving">授权</a-button>
          </div>
        </template>

        <template v-else-if="errorMsg">
          <a-result status="error" title="授权失败" :sub-title="errorMsg">
            <template #extra>
              <a-button @click="window.close()">关闭</a-button>
            </template>
          </a-result>
        </template>
      </a-spin>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { message } from 'ant-design-vue'
import { getOAuthAppInfo, authorizeOAuth, type OAuthAppInfo } from '@/api/oauth2'

const loading = ref(false)
const approving = ref(false)
const appInfo = ref<OAuthAppInfo | null>(null)
const errorMsg = ref('')

const routeParams = new URLSearchParams(window.location.search)
const clientId = routeParams.get('client_id') || ''
const redirectUri = routeParams.get('redirect_uri') || ''
const scope = routeParams.get('scope') || '[]'
const state = routeParams.get('state') || ''

function parseScopes(str: string): string[] {
  try { return JSON.parse(str) } catch { return [] }
}

function scopeLabel(s: string): string {
  const map: Record<string, string> = {
    'user:read': '读取用户信息',
    'mod:read': '查询模组',
    'mod.submit': '提交模组',
    'mod.query': '查询模组',
    'mod.manage': '管理模组',
    'user.manage': '管理用户',
    'system.settings': '系统设置',
    'apikey.manage': '管理API KEY',
    'oauth2.manage': '管理OAuth2',
    'developer.apply': '申请开发者',
  }
  return map[s] || s
}

async function fetchAppInfo() {
  if (!clientId) {
    errorMsg.value = '缺少 client_id 参数'
    return
  }
  loading.value = true
  try {
    const res = await getOAuthAppInfo(clientId)
    if (res.status === 200) {
      appInfo.value = res.data
    } else {
      errorMsg.value = res.message || '应用不存在'
    }
  } catch (err: any) {
    errorMsg.value = err.response?.data?.message || '获取应用信息失败'
  } finally {
    loading.value = false
  }
}

async function handleApprove() {
  approving.value = true
  try {
    const res = await authorizeOAuth({
      clientId,
      redirectUri,
      scope,
      state,
      approved: true,
    })
    if (res.status === 200 && res.data?.redirect_url) {
      window.location.href = res.data.redirect_url
    } else {
      message.error('授权失败')
    }
  } catch (err: any) {
    message.error(err.response?.data?.message || '授权失败')
  } finally {
    approving.value = false
  }
}

function handleDeny() {
  const denyUrl = `${redirectUri}?error=access_denied&state=${encodeURIComponent(state)}`
  window.location.href = denyUrl
}

onMounted(fetchAppInfo)
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
  padding: 40px 32px;
  background: var(--bg-base-default);
  border-radius: var(--radius-lg);
}

.auth-title {
  font-weight: 700;
  text-align: center;
  margin-bottom: 24px;
}
</style>
