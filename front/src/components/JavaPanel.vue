<script lang="ts" setup>
import { ref, computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { message } from 'ant-design-vue';
import { CoffeeOutlined, ReloadOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons-vue';
import { get } from '../utils/api';

const { t } = useI18n();

interface JavaInfo {
  path: string;
  version?: string;
  vendor?: string;
  majorVersion?: number;
}

const versions = ref<JavaInfo[]>([]);
const loading = ref(false);
const error = ref('');

/**
 * 检测系统中的 Java。
 *
 * 这里是原「我的服务端 → Java 检测」标签页的能力，页面删除后迁移到设置中，
 * 因为制作服务端时仍然需要它来判断 Java 是否可用。
 */
async function detect() {
  loading.value = true;
  error.value = '';
  try {
    const response = await get<{ status: number; data: JavaInfo[] }>('/java/detect', 30000);
    versions.value = response.data || [];
    if (versions.value.length === 0) {
      error.value = t('setting.java_not_found');
    } else {
      message.success(t('setting.java_available_count', { count: versions.value.length }));
    }
  } catch (err: any) {
    error.value = err?.message || t('setting.java_detect_failed');
  } finally {
    loading.value = false;
  }
}

const hasResults = computed(() => versions.value.length > 0);

function describe(item: JavaInfo): string {
  const parts: string[] = [];
  if (item.version) parts.push(String(item.version));
  if (item.vendor) parts.push(item.vendor);
  return parts.join(' · ') || '—';
}
</script>

<template>
  <div class="tw-bg-white tw:rounded-2xl tw:shadow-lg tw:p-7 tw:mb-6 tw:border tw:border-gray-100 tw:transition-all duration-300">
    <div class="tw:flex tw:items-center tw:justify-between tw:mb-6 tw:flex-wrap tw:gap-3">
      <h2 class="tw:text-xl tw:font-bold tw:text-gray-800 tw:flex tw:items-center">
        <span class="tw-w-10 tw:h-10 tw:rounded-xl tw:flex tw:items-center tw:justify-center tw:mr-3 tw:shadow-md tw:bg-amber-100 tw:text-amber-800">
          <CoffeeOutlined />
        </span>
        {{ t('setting.java_title') }}
      </h2>

      <a-button size="small" :loading="loading" @click="detect">
        <template #icon><ReloadOutlined /></template>
        {{ loading ? t('setting.java_detecting') : t('setting.java_detect_button') }}
      </a-button>
    </div>

    <p class="tw:text-xs tw:text-gray-500 tw:mb-4">{{ t('setting.java_desc') }}</p>

    <a-alert v-if="error" type="warning" show-icon :message="error" class="tw:mb-3" />

    <div v-if="hasResults" class="tw:flex tw:flex-col tw:gap-2">
      <div
        v-for="(item, index) in versions"
        :key="`${item.path}-${index}`"
        class="tw:flex tw:items-start tw:gap-3 tw:p-3 tw:rounded-xl tw:border tw:border-gray-100 hover:tw:border-emerald-200 hover:tw:bg-emerald-50/40 tw:transition-all"
      >
        <CheckCircleOutlined class="tw:text-emerald-500 tw:mt-0.5 tw:shrink-0" />
        <div class="tw:flex-1 tw:min-w-0">
          <p class="tw:text-sm tw:font-semibold tw:text-gray-800">{{ describe(item) }}</p>
          <p class="tw:text-[11px] tw:text-gray-500 tw:break-all tw:mt-0.5">{{ item.path }}</p>
        </div>
        <a-tag v-if="index === 0" color="green" class="tw:shrink-0">{{ t('setting.java_available') }}</a-tag>
      </div>
    </div>

    <div
      v-else-if="!loading && !error"
      class="tw:flex tw:items-center tw:gap-2 tw:text-sm tw:text-gray-400 tw:py-3"
    >
      <CloseCircleOutlined />
      <span>{{ t('setting.java_not_found') }}</span>
    </div>
  </div>
</template>
