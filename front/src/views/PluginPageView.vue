<script lang="ts" setup>
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import { message } from 'ant-design-vue';
import { ArrowLeftOutlined, SettingOutlined, ReloadOutlined } from '@ant-design/icons-vue';
import { pluginApi, describeError, type PluginDetail } from '../utils/pluginApi';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const pluginId = computed(() => route.params.pluginId as string);
const pageKey = computed(() => route.params.pageKey as string);

const plugin = ref<PluginDetail | null>(null);
const loading = ref(true);
const hasFrontend = ref(false);
const frontendUrl = ref('');
const loadError = ref<string | null>(null);

/**
 * 加载插件页面。
 *
 * 约定：后端在页面存在时返回 `text/html`，不存在时返回 JSON（HTTP 404）。
 * 旧实现无条件按 JSON 解析非 HTML 的响应体，导致 404 时会把错误响应当成
 * 页面数据，兜底界面永远显示不出插件信息；这里改为按状态码分流。
 */
async function loadPage() {
  loading.value = true;
  loadError.value = null;
  releaseFrontendUrl();

  try {
    // 插件详情用于兜底界面展示名称/作者，与页面加载并行，互不阻塞。
    const detailPromise = pluginApi.detail(pluginId.value).catch(() => null);

    const response = await fetch(pluginApi.pageUrl(pluginId.value, pageKey.value));
    const contentType = response.headers.get('content-type') || '';

    if (response.ok && contentType.includes('text/html')) {
      const html = await response.text();
      const blob = new Blob([html], { type: 'text/html' });
      frontendUrl.value = URL.createObjectURL(blob);
      hasFrontend.value = true;
    } else if (response.status === 404) {
      hasFrontend.value = false;
    } else if (!response.ok) {
      hasFrontend.value = false;
      let detail = `HTTP ${response.status}`;
      try {
        const payload = (await response.json()) as { message?: string };
        detail = payload?.message || detail;
      } catch {
        // 响应不是 JSON，保留状态码提示。
      }
      loadError.value = detail;
    } else {
      // 200 但不是 HTML：按 JSON 信封解析。
      const payload = (await response.json()) as { status?: number; message?: string };
      hasFrontend.value = false;
      if (payload?.status && payload.status >= 400) {
        loadError.value = payload.message || null;
      }
    }

    plugin.value = await detailPromise;
  } catch (error) {
    loadError.value = describeError(error, '加载页面失败');
    message.error(loadError.value);
  } finally {
    loading.value = false;
  }
}

function releaseFrontendUrl() {
  if (frontendUrl.value) {
    URL.revokeObjectURL(frontendUrl.value);
    frontendUrl.value = '';
  }
}

function goBack() {
  router.push('/plugins');
}

function goToPluginDetail() {
  router.push(`/plugin/${pluginId.value}`);
}

/** 开发插件时改完 HTML 可以直接刷新，不必来回切路由。 */
function refreshPage() {
  loadPage();
}

onMounted(() => {
  loadPage();
});

onBeforeUnmount(() => {
  releaseFrontendUrl();
});

watch(() => [pluginId.value, pageKey.value], () => {
  loadPage();
});
</script>

<template>
  <div class="tw:h-full tw:w-full tw:flex tw:flex-col tw:overflow-hidden">
    <div v-if="loading" class="tw:flex tw:items-center tw:justify-center tw:h-full">
      <a-spin />
    </div>

    <template v-else-if="hasFrontend">
      <div class="tw:flex tw:items-center tw:gap-2 tw:p-3 tw:bg-white tw:border-b tw:border-gray-100">
        <a-button type="text" @click="goBack">
          <template #icon><ArrowLeftOutlined /></template>
        </a-button>
        <span class="tw:text-sm tw:font-medium">{{ plugin?.manifest.name || pageKey }}</span>
        <span class="tw:text-xs tw:text-gray-400">{{ pageKey }}</span>
        <div class="tw:flex-1" />
        <a-button size="small" class="tw:inline-flex tw:items-center" @click="refreshPage">
          <template #icon><ReloadOutlined /></template>
          <span>刷新</span>
        </a-button>
        <a-button size="small" class="tw:inline-flex tw:items-center" @click="goToPluginDetail">
          <template #icon><SettingOutlined /></template>
          <span>{{ t('plugin.config_title') || '配置' }}</span>
        </a-button>
      </div>
      <iframe
        :src="frontendUrl"
        class="tw:flex-1 tw:w-full tw:border-none"
        sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
      />
    </template>

    <div v-else class="tw:h-full tw:overflow-y-auto tw:p-8">
      <div class="tw:max-w-4xl tw:mx-auto">
        <div class="tw:flex tw:items-center tw:gap-3 tw:mb-6">
          <a-button type="text" @click="goBack">
            <template #icon><ArrowLeftOutlined /></template>
          </a-button>
          <div>
            <h1 class="tw:text-xl tw:font-bold tw:text-gray-800">{{ plugin?.manifest.name || pageKey }}</h1>
            <p class="tw:text-sm tw:text-gray-500">
              {{ plugin?.manifest.author }} · v{{ plugin?.manifest.version }}
            </p>
          </div>
          <div class="tw:flex-1" />
          <a-button @click="goToPluginDetail">
            <template #icon><SettingOutlined /></template>
            {{ t('plugin.config_title') || '配置' }}
          </a-button>
        </div>

        <a-alert
          v-if="loadError"
          type="error"
          show-icon
          class="tw:mb-4"
          message="加载插件页面失败"
          :description="loadError"
        />

        <a-card class="tw:mb-6">
          <div class="tw:text-center tw:py-12">
            <div class="tw:text-5xl tw:mb-4 tw:opacity-30">📄</div>
            <h3 class="tw:text-lg tw:font-medium tw:text-gray-600 tw:mb-2">
              该页面暂无自定义界面
            </h3>
            <p class="tw:text-sm tw:text-gray-400 tw:mb-6">
              在插件目录中创建 <code class="tw:bg-gray-100 tw:px-1 tw:rounded">frontend/{{ pageKey }}.html</code>
              或 <code class="tw:bg-gray-100 tw:px-1 tw:rounded">frontend/{{ pageKey }}/index.html</code>
              即可替换此默认页面。
            </p>

            <div class="tw:bg-gray-50 tw:rounded-lg tw:p-4 tw:text-left">
              <p class="tw:text-xs tw:text-gray-500 tw:mb-2 tw:font-medium">插件信息</p>
              <div class="tw:grid tw:grid-cols-2 tw:gap-2 tw:text-sm">
                <div><span class="tw:text-gray-400">ID：</span>{{ plugin?.manifest.id || pluginId }}</div>
                <div><span class="tw:text-gray-400">名称：</span>{{ plugin?.manifest.name || '—' }}</div>
                <div><span class="tw:text-gray-400">作者：</span>{{ plugin?.manifest.author || '—' }}</div>
                <div><span class="tw:text-gray-400">版本：</span>{{ plugin?.manifest.version || '—' }}</div>
                <div><span class="tw:text-gray-400">页面：</span>{{ pageKey }}</div>
              </div>
            </div>

            <div
              v-if="plugin?.manifest.defaultConfig && Object.keys(plugin.manifest.defaultConfig).length > 0"
              class="tw:mt-4 tw:bg-blue-50 tw:rounded-lg tw:p-4 tw:text-left"
            >
              <p class="tw:text-xs tw:text-blue-600 tw:mb-2 tw:font-medium">默认配置</p>
              <div class="tw:grid tw:grid-cols-2 tw:gap-2 tw:text-sm">
                <div
                  v-for="(value, key) in plugin.manifest.defaultConfig"
                  :key="key"
                  class="tw:flex tw:items-center tw:gap-2"
                >                  <span class="tw:text-blue-500">{{ key }}：</span>
                  <a-tag v-if="typeof value === 'boolean'" :color="value ? 'green' : 'default'">
                    {{ value ? '是' : '否' }}
                  </a-tag>
                  <span v-else class="tw:text-gray-700">{{ value }}</span>
                </div>
              </div>
            </div>
          </div>
        </a-card>
      </div>
    </div>
  </div>
</template>
