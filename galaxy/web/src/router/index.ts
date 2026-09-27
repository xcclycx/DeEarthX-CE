import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: () => import('@/components/AppLayout.vue'),
      children: [
        {
          path: '',
          name: 'home',
          component: () => import('@/views/HomeView.vue'),
        },
        {
          path: 'search',
          name: 'search',
          component: () => import('@/views/SearchView.vue'),
        },
        {
          path: 'mod/:modId',
          name: 'mod-detail',
          component: () => import('@/views/ModDetailView.vue'),
        },
        {
          path: 'dashboard',
          name: 'dashboard',
          component: () => import('@/views/DashboardView.vue'),
          meta: { requiresAuth: true },
        },
        {
          path: 'mod-submit',
          name: 'mod-submit',
          component: () => import('@/views/ModSubmitView.vue'),
          meta: { requiresAuth: true, permission: 'mod.submit' },
        },
        {
          path: 'developer/apply',
          name: 'developer-apply',
          component: () => import('@/views/DeveloperApplyView.vue'),
          meta: { requiresAuth: true },
        },
        {
          path: 'developer/apps',
          name: 'developer-apps',
          component: () => import('@/views/DeveloperAppsView.vue'),
          meta: { requiresAuth: true, requiresDeveloper: true },
        },
        {
          path: 'admin/review',
          name: 'admin-review',
          component: () => import('@/views/AdminReviewView.vue'),
          meta: { requiresAuth: true, permission: 'mod.manage' },
        },
        {
          path: 'admin/mods',
          name: 'admin-mods',
          component: () => import('@/views/AdminModsView.vue'),
          meta: { requiresAuth: true, permission: 'mod.manage' },
        },
        {
          path: 'admin/users',
          name: 'admin-users',
          component: () => import('@/views/AdminUsersView.vue'),
          meta: { requiresAuth: true, permission: 'user.manage' },
        },
        {
          path: 'admin/developers',
          name: 'admin-developers',
          component: () => import('@/views/AdminDevelopersView.vue'),
          meta: { requiresAuth: true, permission: 'user.manage' },
        },
        {
          path: 'admin/oauth-apps',
          name: 'admin-oauth-apps',
          component: () => import('@/views/AdminOAuthAppsView.vue'),
          meta: { requiresAuth: true, permission: 'oauth2.manage' },
        },
        {
          path: 'admin/settings',
          name: 'admin-settings',
          component: () => import('@/views/AdminSettingsView.vue'),
          meta: { requiresAuth: true, permission: 'system.settings' },
        },
      ],
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/RegisterView.vue'),
    },
    {
      path: '/oauth2/authorize',
      name: 'oauth-authorize',
      component: () => import('@/views/OAuthAuthorizeView.vue'),
    },
  ],
})

router.beforeEach(async (to, _from, next) => {
  const auth = useAuthStore()

  if (auth.isLoggedIn && !auth.user) {
    await auth.fetchUser()
  }

  if (to.meta.requiresAuth && !auth.isLoggedIn) {
    return next({ name: 'login', query: { redirect: to.fullPath } })
  }

  if (to.meta.requiresDeveloper && !auth.isDeveloper) {
    return next({ name: 'home' })
  }

  if (to.meta.permission && !auth.hasPermission(to.meta.permission as string)) {
    return next({ name: 'home' })
  }

  next()
})

export default router
