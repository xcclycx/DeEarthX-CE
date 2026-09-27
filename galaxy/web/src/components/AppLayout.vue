<template>
  <a-layout style="min-height: 100vh">
    <a-layout-sider
      v-model:collapsed="collapsed"
      :trigger="null"
      collapsible
      width="220"
      theme="light"
      style="background: var(--bg-base-default)"
    >
      <div class="logo" @click="$router.push('/')">
        <span class="logo-text brand-gradient-text">Galaxy</span>
      </div>
      <a-menu
        v-model:selectedKeys="selectedKeys"
        mode="inline"
        style="border-right: none"
      >
        <a-menu-item key="home" @click="$router.push('/')">
          <template #icon><HomeOutlined /></template>
          <span>首页</span>
        </a-menu-item>
        <a-menu-item key="search" @click="$router.push('/search')">
          <template #icon><SearchOutlined /></template>
          <span>搜索</span>
        </a-menu-item>
        <a-menu-item v-if="auth.isLoggedIn" key="dashboard" @click="$router.push('/dashboard')">
          <template #icon><DashboardOutlined /></template>
          <span>控制台</span>
        </a-menu-item>
        <a-menu-item v-if="auth.isLoggedIn && auth.hasPermission('mod.submit')" key="mod-submit" @click="$router.push('/mod-submit')">
          <template #icon><CloudUploadOutlined /></template>
          <span>模组提交</span>
        </a-menu-item>
        <a-sub-menu v-if="auth.isLoggedIn" key="developer">
          <template #icon><CodeOutlined /></template>
          <template #title>开发者选项</template>
          <a-menu-item v-if="!auth.isDeveloper" key="developer-apply" @click="$router.push('/developer/apply')">
            <template #icon><FormOutlined /></template>
            <span>申请成为开发者</span>
          </a-menu-item>
          <a-menu-item v-if="auth.isDeveloper" key="developer-status" @click="$router.push('/developer/apply')">
            <template #icon><IdcardOutlined /></template>
            <span>开发者状态</span>
          </a-menu-item>
          <a-menu-item v-if="auth.isDeveloper" key="developer-apps" @click="$router.push('/developer/apps')">
            <template #icon><ApiOutlined /></template>
            <span>管理应用</span>
          </a-menu-item>
        </a-sub-menu>
        <a-sub-menu v-if="showAdmin" key="admin">
          <template #icon><SettingOutlined /></template>
          <template #title>管理</template>
          <a-menu-item v-if="auth.hasPermission('mod.manage')" key="admin-review" @click="$router.push('/admin/review')">
            <template #icon><AuditOutlined /></template>
            <span>模组审核</span>
          </a-menu-item>
          <a-menu-item v-if="auth.hasPermission('mod.manage')" key="admin-mods" @click="$router.push('/admin/mods')">
            <template #icon><AppstoreOutlined /></template>
            <span>模组管理</span>
          </a-menu-item>
          <a-menu-item v-if="auth.hasPermission('user.manage')" key="admin-users" @click="$router.push('/admin/users')">
            <template #icon><TeamOutlined /></template>
            <span>用户管理</span>
          </a-menu-item>
          <a-menu-item v-if="auth.hasPermission('user.manage')" key="admin-developers" @click="$router.push('/admin/developers')">
            <template #icon><CodeOutlined /></template>
            <span>开发者管理</span>
          </a-menu-item>
          <a-menu-item v-if="auth.hasPermission('oauth2.manage')" key="admin-oauth-apps" @click="$router.push('/admin/oauth-apps')">
            <template #icon><ApiOutlined /></template>
            <span>OAuth 应用管理</span>
          </a-menu-item>
          <a-menu-item v-if="auth.hasPermission('system.settings')" key="admin-settings" @click="$router.push('/admin/settings')">
            <template #icon><ToolOutlined /></template>
            <span>系统设置</span>
          </a-menu-item>
        </a-sub-menu>
      </a-menu>
    </a-layout-sider>
    <a-layout>
      <a-layout-header class="layout-header">
        <div class="header-left">
          <component
            :is="collapsed ? MenuUnfoldOutlined : MenuFoldOutlined"
            class="trigger"
            @click="collapsed = !collapsed"
          />
        </div>
        <div class="header-right">
          <template v-if="auth.isLoggedIn">
            <span class="user-name">{{ auth.user?.username }}</span>
            <a-button type="text" size="small" @click="handleLogout">退出</a-button>
          </template>
          <template v-else>
            <a-button type="link" size="small" @click="$router.push('/login')">登录</a-button>
            <a-button type="link" size="small" @click="$router.push('/register')">注册</a-button>
          </template>
        </div>
      </a-layout-header>
      <a-layout-content class="layout-content">
        <router-view />
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import {
  HomeOutlined,
  SearchOutlined,
  DashboardOutlined,
  SettingOutlined,
  AppstoreOutlined,
  AuditOutlined,
  TeamOutlined,
  ToolOutlined,
  MenuUnfoldOutlined,
  MenuFoldOutlined,
  CloudUploadOutlined,
  CodeOutlined,
  FormOutlined,
  ApiOutlined,
  IdcardOutlined,
} from '@ant-design/icons-vue'

const route = useRoute()
const auth = useAuthStore()
const collapsed = ref(false)

const selectedKeys = ref<string[]>(['home'])

const showAdmin = computed(() => {
  return auth.hasAnyPermission(['mod.manage', 'user.manage', 'system.settings', 'oauth2.manage'])
})

function updateSelectedKeys() {
  const path = route.path
  if (path === '/') selectedKeys.value = ['home']
  else if (path === '/search') selectedKeys.value = ['search']
  else if (path === '/dashboard') selectedKeys.value = ['dashboard']
  else if (path === '/mod-submit') selectedKeys.value = ['mod-submit']
  else if (path.startsWith('/developer/apply')) selectedKeys.value = ['developer-apply']
  else if (path.startsWith('/developer/apps')) selectedKeys.value = ['developer-apps']
  else if (path.startsWith('/admin/review')) selectedKeys.value = ['admin-review']
  else if (path.startsWith('/admin/mods')) selectedKeys.value = ['admin-mods']
  else if (path.startsWith('/admin/users')) selectedKeys.value = ['admin-users']
  else if (path.startsWith('/admin/developers')) selectedKeys.value = ['admin-developers']
  else if (path.startsWith('/admin/oauth-apps')) selectedKeys.value = ['admin-oauth-apps']
  else if (path.startsWith('/admin/settings')) selectedKeys.value = ['admin-settings']
  else selectedKeys.value = []
}

watch(() => route.path, updateSelectedKeys, { immediate: true })

function handleLogout() {
  auth.logout()
  window.location.href = '/login'
}
</script>

<style scoped>
.logo {
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  border-bottom: 1px solid var(--border-neutral-l1);
}

.logo-text {
  font-size: 22px;
  font-weight: 700;
  letter-spacing: -0.5px;
}

.layout-header {
  background: var(--bg-base-default);
  padding: 0 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--border-neutral-l1);
  height: 56px;
  line-height: 56px;
}

.header-left {
  display: flex;
  align-items: center;
}

.trigger {
  font-size: 18px;
  cursor: pointer;
  color: var(--text-secondary);
  transition: color 0.2s;
}

.trigger:hover {
  color: var(--bg-brand);
}

.header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.user-name {
  color: var(--text-secondary);
  font-size: 14px;
}

.layout-content {
  background: var(--bg-base-secondary);
  min-height: calc(100vh - 56px);
}
</style>
