<script lang="ts" setup>
import { ToolOutlined, ExportOutlined, DeleteOutlined, LockOutlined } from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();

interface PluginManifest {
  id: string;
  name: string;
  version: string;
  author: string;
  description?: string;
  icon?: string;
}

interface PluginInfo {
  manifest: PluginManifest;
  enabled: boolean;
  config: { enabled: boolean; settings: Record<string, any> };
}

defineProps<{
  plugin: PluginInfo;
}>();

const emit = defineEmits<{
  navigateToDetail: [plugin: PluginInfo];
  togglePlugin: [pluginId: string, enable: boolean];
  exportPlugin: [plugin: PluginInfo];
  openEncryptExport: [plugin: PluginInfo];
  deletePlugin: [plugin: PluginInfo];
}>();
</script>

<template>
  <div
    class="tw:bg-white tw:rounded-xl tw:shadow-sm tw:border tw:border-gray-100 tw:p-5 tw:cursor-pointer tw:transition-all tw:duration-300 hover:tw:shadow-md hover:tw:border-gray-200 tw:flex tw:flex-col"
    @click="emit('navigateToDetail', plugin)"
  >
    <div class="tw:flex tw:items-start tw:justify-between tw:mb-3">
      <div class="tw:flex tw:items-center tw:gap-3 tw:flex-1 tw:min-w-0">
        <div v-if="plugin.manifest.icon" class="tw:w-9 tw:h-9 tw:rounded-lg tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-lg tw:shrink-0">
          {{ plugin.manifest.icon }}
        </div>
        <div v-else class="tw:w-9 tw:h-9 tw:rounded-lg tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-base tw:shrink-0">
          <ToolOutlined class="tw:text-blue-500" />
        </div>
        <div class="tw:min-w-0">
          <h3 class="tw:text-base tw:font-semibold tw:text-gray-800 tw:truncate">{{ plugin.manifest.name }}</h3>
          <p class="tw:text-xs tw:text-gray-500 tw:mt-0.5">
            {{ plugin.manifest.author }} · v{{ plugin.manifest.version }}
          </p>
        </div>
      </div>
      <a-tag
        :color="plugin.enabled ? 'green' : 'default'"
        class="tw:shrink-0 tw:ml-2"
      >
        {{ plugin.enabled ? t('plugin.plugin_status_enabled') : t('plugin.plugin_status_disabled') }}
      </a-tag>
    </div>

    <p v-if="plugin.manifest.description" class="tw:text-sm tw:text-gray-600 tw:line-clamp-2 tw:mb-4 tw:flex-1">
      {{ plugin.manifest.description }}
    </p>

    <div class="tw:flex tw:items-center tw:gap-2 tw:pt-3 tw:border-t tw:border-gray-50" @click.stop>
      <a-switch
        :checked="plugin.enabled"
        @change="(checked: boolean) => emit('togglePlugin', plugin.manifest.id, checked)"
        size="small"
      />
      <span class="tw:text-xs tw:text-gray-500">
        {{ plugin.enabled ? t('common.start') : t('common.close') }}
      </span>
      <div class="tw:flex-1" />
      <a-button size="small" type="text" @click="emit('exportPlugin', plugin)" :title="t('plugin.export_button')">
        <template #icon><ExportOutlined /></template>
      </a-button>
      <a-button size="small" type="text" @click.stop="emit('openEncryptExport', plugin)" title="加密导出">
        <template #icon><LockOutlined /></template>
      </a-button>
      <a-button size="small" type="text" danger @click="emit('deletePlugin', plugin)" :title="t('plugin.delete_button')">
        <template #icon><DeleteOutlined /></template>
      </a-button>
    </div>
  </div>
</template>