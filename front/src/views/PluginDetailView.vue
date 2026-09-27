<script lang="ts" setup>
import { ref, onMounted, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { message, Modal } from 'ant-design-vue';
import {
  ArrowLeftOutlined,
  GlobalOutlined,
  CodeOutlined,
  ExportOutlined,
  DeleteOutlined,
  SettingOutlined,
  ToolOutlined,
  ReloadOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined
} from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';
import {
  pluginApi,
  describeError,
  type PluginDetail,
  type PluginSettingField
} from '../utils/pluginApi';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const pluginId = computed(() => route.params.id as string);
const plugin = ref<PluginDetail | null>(null);
const loading = ref(false);
const saving = ref(false);
const reloading = ref(false);
const configSettings = ref<Record<string, unknown>>({});
const defaults = ref<Record<string, unknown>>({});
const schema = ref<PluginSettingField[]>([]);
const validationErrors = ref<string[]>([]);

/** 配置项展示顺序：优先用后端返回的 schema（含控件类型与标签）。 */
const configFields = computed<PluginSettingField[]>(() => {
  if (schema.value.length > 0) {
    return schema.value;
  }

  const fallback = plugin.value?.manifest.defaultConfig ?? {};
  return Object.keys(fallback).map((key) => ({
    key,
    type: inferFieldType(fallback[key])
  }));
});

const hasConfig = computed(() => configFields.value.length > 0);
const hooks = computed(() => plugin.value?.manifest.capabilities?.hooks ?? []);
const capabilities = computed(() => plugin.value?.manifest.capabilities);
const dependencies = computed(() => plugin.value?.manifest.dependencies ?? []);

function inferFieldType(value: unknown): string {
  if (typeof value === 'boolean') return 'boolean';
  if (typeof value === 'number') return 'number';
  if (Array.isArray(value)) return 'list';
  if (value !== null && typeof value === 'object') return 'object';
  return 'string';
}

function fieldLabel(field: PluginSettingField): string {
  return field.label || plugin.value?.manifest.configLabels?.[field.key] || field.key;
}

function fieldDescription(field: PluginSettingField): string {
  return field.description || plugin.value?.manifest.configDescriptions?.[field.key] || '';
}

/** 仅把表单值恢复成清单默认值（不落盘，用户仍可选择保存）。 */
function resetSettingsToDefaults() {
  configSettings.value = JSON.parse(JSON.stringify(defaults.value));
}

async function loadPlugin() {
  loading.value = true;
  try {
    plugin.value = await pluginApi.detail(pluginId.value);
    configSettings.value = { ...(plugin.value.config?.settings || {}) };
  } catch (error) {
    message.error(describeError(error, t('common.error')));
    router.push('/plugins');
  } finally {
    loading.value = false;
  }
}

async function loadPluginConfig() {
  try {
    const payload = await pluginApi.getConfig(pluginId.value);
    defaults.value = payload.defaults || {};
    configSettings.value = { ...(payload.settings || {}) };
    schema.value = payload.schema || [];
  } catch {
    // 配置接口失败时保留清单里的 defaultConfig 作为兜底展示。
  }
}

async function validatePlugin() {
  try {
    const result = await pluginApi.validate(pluginId.value);
    validationErrors.value = [...(result.errors || []), ...(result.warnings || [])];

    if (result.isValid && validationErrors.value.length === 0) {
      message.success('插件校验通过');
    } else if (result.isValid) {
      message.warning('插件校验通过，但有建议项');
    } else {
      message.error('插件校验未通过，详见提示');
    }
  } catch (error) {
    message.error(describeError(error, '校验失败'));
  }
}

async function saveConfig() {
  saving.value = true;
  try {
    await pluginApi.saveConfig(pluginId.value, configSettings.value);
    message.success(t('plugin.config_saved'));
  } catch (error) {
    message.error(describeError(error, t('plugin.config_save_failed')));
  } finally {
    saving.value = false;
  }
}

async function resetConfig() {
  try {
    await pluginApi.resetConfig(pluginId.value);
    await loadPluginConfig();
    message.success('已重置为默认配置');
  } catch (error) {
    message.error(describeError(error, '重置配置失败'));
  }
}

async function togglePlugin() {
  if (!plugin.value) return;

  const enable = !plugin.value.enabled;
  try {
    if (enable) {
      await pluginApi.enable(pluginId.value);
    } else {
      await pluginApi.disable(pluginId.value);
    }

    message.success(enable ? t('plugin.enable_success') : t('plugin.disable_success'));
    await loadPlugin();
  } catch (error) {
    message.error(describeError(error, enable ? t('plugin.enable_failed') : t('plugin.disable_failed')));
  }
}

/** 暂停/恢复参与流水线，但不改变启用状态与配置。 */
async function toggleAutoStart() {
  if (!plugin.value) return;

  const next = !plugin.value.autoStart;
  try {
    await pluginApi.setAutoStart(pluginId.value, next);
    message.success(next ? '插件已恢复运行' : '插件已暂停运行');
    await loadPlugin();
  } catch (error) {
    message.error(describeError(error, '切换运行状态失败'));
  }
}

async function reloadPlugin() {
  reloading.value = true;
  try {
    await pluginApi.reload(pluginId.value);
    message.success('插件已重新加载');
    await loadPlugin();
    await loadPluginConfig();
  } catch (error) {
    message.error(describeError(error, '重新加载失败'));
  } finally {
    reloading.value = false;
  }
}

function exportPlugin() {
  try {
    pluginApi.exportZip(pluginId.value);
    message.success(t('plugin.export_success'));
  } catch (error) {
    message.error(describeError(error, t('plugin.export_failed')));
  }
}

function deletePlugin() {
  const target = plugin.value;
  if (!target) return;

  Modal.confirm({
    title: t('plugin.delete_title'),
    content: t('plugin.delete_confirm', { name: target.manifest.name }),
    okText: t('common.delete'),
    cancelText: t('common.cancel'),
    okButtonProps: { danger: true },
    onOk: async () => {
      try {
        await pluginApi.uninstall(pluginId.value, true);
        message.success(t('plugin.delete_success'));
        router.push('/plugins');
      } catch (error) {
        message.error(describeError(error, t('plugin.delete_failed')));
      }
    }
  });
}

onMounted(async () => {
  await loadPlugin();
  await loadPluginConfig();
});
</script>

<template>
  <div class="tw:h-full tw:w-full tw:flex tw:flex-col tw:p-6 tw:overflow-y-auto">
    <div v-if="loading" class="tw:flex tw:items-center tw:justify-center tw:h-full">
      <a-spin />
    </div>

    <template v-else-if="plugin">
      <div class="tw:flex tw:items-center tw:gap-3 tw:mb-6">
        <a-button type="text" class="tw:inline-flex tw:items-center tw:justify-center" @click="router.push('/plugins')">
          <template #icon><ArrowLeftOutlined /></template>
        </a-button>
        <div v-if="plugin.manifest.icon" class="tw:w-10 tw:h-10 tw:rounded-xl tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-xl">
          {{ plugin.manifest.icon }}
        </div>
        <div v-else class="tw:w-10 tw:h-10 tw:rounded-xl tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-lg">
          <ToolOutlined class="tw:text-blue-500" />
        </div>
        <div>
          <h1 class="tw:text-2xl tw:font-bold tw:text-gray-800">{{ plugin.manifest.name }}</h1>
          <p class="tw:text-sm tw:text-gray-500">{{ plugin.manifest.author }} · v{{ plugin.manifest.version }}</p>
        </div>
        <div class="tw:flex-1" />
        <a-switch :checked="plugin.enabled" @change="togglePlugin" />
        <a-button class="tw:inline-flex tw:items-center" :loading="reloading" @click="reloadPlugin">
          <template #icon><ReloadOutlined /></template>
          <span>重新加载</span>
        </a-button>
        <a-button class="tw:inline-flex tw:items-center" @click="exportPlugin">
          <template #icon><ExportOutlined /></template>
          <span>{{ t('plugin.export_button') }}</span>
        </a-button>
        <a-button danger class="tw:inline-flex tw:items-center" @click="deletePlugin">
          <template #icon><DeleteOutlined /></template>
          <span>{{ t('plugin.delete_button') }}</span>
        </a-button>
      </div>

      <a-alert
        v-if="plugin.error"
        type="warning"
        show-icon
        class="tw:mb-4"
        message="此插件可能无法正常运行"
        :description="plugin.error"
      />

      <a-alert
        v-if="validationErrors.length"
        type="info"
        show-icon
        closable
        class="tw:mb-4"
        message="校验结果"
        @close="validationErrors = []"
      >
        <template #description>
          <ul class="tw:m-0 tw:pl-4 tw:text-xs">
            <li v-for="item in validationErrors" :key="item">{{ item }}</li>
          </ul>
        </template>
      </a-alert>

      <div class="tw:grid tw:grid-cols-1 tw:lg:grid-cols-3 tw:gap-6">
        <div class="tw:lg:col-span-1">
          <a-card :title="t('plugin.plugin_info')" class="tw:mb-4">
            <a-descriptions :column="1" size="small">
              <a-descriptions-item :label="t('plugin.plugin_id')">
                <a-tag>{{ plugin.manifest.id }}</a-tag>
              </a-descriptions-item>
              <a-descriptions-item :label="t('plugin.plugin_name')">{{ plugin.manifest.name }}</a-descriptions-item>
              <a-descriptions-item :label="t('plugin.plugin_version')">{{ plugin.manifest.version }}</a-descriptions-item>
              <a-descriptions-item :label="t('plugin.plugin_author')">{{ plugin.manifest.author }}</a-descriptions-item>
              <a-descriptions-item v-if="plugin.manifest.homepage || plugin.manifest.url" :label="t('plugin.plugin_url')">
                <a :href="plugin.manifest.homepage || plugin.manifest.url" target="_blank" class="tw:text-blue-500">
                  <GlobalOutlined class="tw:mr-1" />{{ plugin.manifest.homepage || plugin.manifest.url }}
                </a>
              </a-descriptions-item>
              <a-descriptions-item v-if="plugin.manifest.sourceUrl" :label="t('plugin.plugin_source_url')">
                <a :href="plugin.manifest.sourceUrl" target="_blank" class="tw:text-blue-500">
                  <CodeOutlined class="tw:mr-1" />{{ plugin.manifest.sourceUrl }}
                </a>
              </a-descriptions-item>
              <a-descriptions-item label="加载状态">
                <a-tag :color="plugin.state === 'Ready' ? 'green' : 'orange'">{{ plugin.state }}</a-tag>
              </a-descriptions-item>
              <a-descriptions-item label="参与流水线">
                <a-tag :color="plugin.autoStart ? 'green' : 'default'">{{ plugin.autoStart ? '是' : '已暂停' }}</a-tag>
              </a-descriptions-item>
            </a-descriptions>

            <a-button class="tw:mt-3 tw:w-full tw:inline-flex tw:items-center tw:justify-center" @click="toggleAutoStart">
              <template #icon>
                <PlayCircleOutlined v-if="!plugin.autoStart" />
                <PauseCircleOutlined v-else />
              </template>
              <span>{{ plugin.autoStart ? '暂停参与流水线' : '恢复参与流水线' }}</span>
            </a-button>
          </a-card>

          <a-card v-if="plugin.manifest.description" :title="t('template.description')" class="tw:mb-4">
            <p class="tw:text-sm tw:text-gray-600">{{ plugin.manifest.description }}</p>
          </a-card>

          <a-card v-if="hooks.length || capabilities?.filterStrategies?.length" title="扩展能力" class="tw:mb-4">
            <div v-if="hooks.length" class="tw:mb-2">
              <p class="tw:text-xs tw:text-gray-400 tw:mb-1">声明的钩子</p>
              <div class="tw:flex tw:flex-wrap tw:gap-1">
                <a-tag v-for="hook in hooks" :key="hook" color="geekblue">{{ hook }}</a-tag>
              </div>
            </div>
            <div v-if="capabilities?.filterStrategies?.length">
              <p class="tw:text-xs tw:text-gray-400 tw:mb-1">过滤策略</p>
              <div class="tw:flex tw:flex-wrap tw:gap-1">
                <a-tag v-for="strategy in capabilities.filterStrategies" :key="strategy.name" color="purple">
                  {{ strategy.label || strategy.name }}
                </a-tag>
              </div>
            </div>
          </a-card>

          <a-card v-if="dependencies.length" title="依赖" class="tw:mb-4">
            <div v-for="dependency in dependencies" :key="dependency.id" class="tw:flex tw:items-center tw:justify-between tw:py-1">
              <span class="tw:text-sm">{{ dependency.id }}</span>
              <a-tag :color="dependency.optional ? 'default' : 'blue'" class="tw:text-xs">
                {{ dependency.optional ? '可选' : dependency.version || '必需' }}
              </a-tag>
            </div>
          </a-card>

          <a-card v-if="plugin.manifest.sidebarItems?.length" :title="t('plugin.plugin_sidebar_items')" class="tw:mb-4">
            <div v-for="item in plugin.manifest.sidebarItems" :key="item.key" class="tw:py-1 tw:flex tw:items-center tw:gap-2">
              <a-tag color="blue">{{ item.label }}</a-tag>
              <a class="tw:text-xs tw:text-blue-500" @click="router.push(`/plugin-page/${pluginId}/${item.route}`)">
                {{ item.route }}
              </a>
            </div>
          </a-card>

          <a-button block @click="validatePlugin">校验插件</a-button>
        </div>

        <div class="tw:lg:col-span-2">
          <a-card :title="t('plugin.config_title')">
            <div v-if="!hasConfig" class="tw:text-center tw:py-8 tw:text-gray-400">
              <SettingOutlined class="tw:text-3xl tw:mb-2" />
              <p>{{ t('plugin.config_no_config') }}</p>
              <p class="tw:text-xs tw:mt-2">在 manifest.json 的 defaultConfig 中声明配置项后即可在此修改。</p>
            </div>

            <a-form v-else :label-col="{ span: 6 }" :wrapper-col="{ span: 18 }">
              <a-form-item v-for="field in configFields" :key="field.key">
                <template #label>
                  <a-tooltip v-if="fieldDescription(field)" :title="fieldDescription(field)">
                    <span>{{ fieldLabel(field) }}</span>
                  </a-tooltip>
                  <span v-else>{{ fieldLabel(field) }}</span>
                </template>

                <a-switch
                  v-if="field.type === 'boolean'"
                  v-model:checked="configSettings[field.key]"
                />
                <a-input-number
                  v-else-if="field.type === 'number'"
                  v-model:value="configSettings[field.key]"
                  :min="field.min"
                  :max="field.max"
                  class="tw:w-full"
                />
                <a-select
                  v-else-if="field.type === 'select' && field.options?.length"
                  v-model:value="configSettings[field.key]"
                  :options="field.options"
                  class="tw:w-full"
                />
                <a-input-password
                  v-else-if="field.type === 'password'"
                  v-model:value="configSettings[field.key]"
                  class="tw:w-full"
                />
                <a-textarea
                  v-else-if="field.type === 'textarea'"
                  v-model:value="configSettings[field.key]"
                  :rows="3"
                  class="tw:w-full"
                />
                <a-input
                  v-else
                  v-model:value="configSettings[field.key]"
                  :placeholder="String(defaults[field.key] ?? '')"
                />
              </a-form-item>

              <a-form-item :wrapper-col="{ offset: 6, span: 18 }">
                <div class="tw:flex tw:gap-2">
                  <a-button type="primary" :loading="saving" @click="saveConfig">
                    {{ t('plugin.config_save') }}
                  </a-button>
                  <a-button @click="resetSettingsToDefaults">恢复默认值</a-button>
                  <a-button danger @click="resetConfig">重置已保存配置</a-button>
                </div>
              </a-form-item>
            </a-form>
          </a-card>
        </div>
      </div>
    </template>
  </div>
</template>
