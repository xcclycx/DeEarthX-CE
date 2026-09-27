<script lang="ts" setup>
import { ref, watch, onMounted, computed } from 'vue';
import { message } from 'ant-design-vue';
import { useI18n } from 'vue-i18n';
import { ExperimentOutlined } from '@ant-design/icons-vue';
import { setLanguage, type Language } from '../utils/i18n';
import axios from '../utils/axios';
import { showAds, setShowAds } from '../stores/settingsStore';
import LogsPanel from '../components/LogsPanel.vue';
import JavaPanel from '../components/JavaPanel.vue';

interface OptimizationConfig {
  enabled: boolean;
  generateLaunchScript: boolean;
  aikarFlags: boolean;
  stripClientResources: boolean;
  sanitizeSensitive: boolean;
  detectRedundant: boolean;
  generateServerProperties: boolean;
  memoryMb: number;
  backup: boolean;
  dryRun: boolean;
}

interface AppConfig {
  mirror: {
    bmclapi: boolean;
    mcimirror: boolean;
    mcimirrorModrinthOnly?: boolean;
  };
  filter: {
    hashes: boolean;
    dexpub: boolean;
    mixins: boolean;
    modrinth: boolean;
  };
  oaf: boolean;
  autoZip: boolean;
  javaPath?: string;
  optimization: OptimizationConfig;
}

interface SettingItem {
  key: string;
  name: string;
  description: string;
  path: string;
  defaultValue: boolean;
}

interface SettingCategory {
  id: string;
  title: string;
  icon: string;
  bgColor: string;
  textColor: string;
  items: SettingItem[];
  hasJava?: boolean;
  hasLanguage?: boolean;
  hasMemory?: boolean;
  hasPreview?: boolean;
}

/** 优化配置的默认值，与后端 DeEarthXConfig 保持一致。 */
function createDefaultOptimization(): OptimizationConfig {
  return {
    enabled: true,
    generateLaunchScript: true,
    aikarFlags: true,
    stripClientResources: true,
    sanitizeSensitive: true,
    detectRedundant: true,
    generateServerProperties: true,
    memoryMb: 0,
    backup: true,
    dryRun: false
  };
}

const config = ref<AppConfig>({
  mirror: { bmclapi: false, mcimirror: false },
  filter: { hashes: false, dexpub: false, mixins: false, modrinth: false },
  oaf: false,
  autoZip: false,
  javaPath: undefined,
  optimization: createDefaultOptimization()
});

const settings = computed<SettingCategory[]>(() => {
  return [
    {
      id: 'filter',
      title: t('setting.category_filter'),
      icon: '',
      bgColor: 'bg-emerald-100',
      textColor: 'text-emerald-800',
      items: [
        {
          key: 'hashes',
          name: t('setting.filter_hashes_name'),
          description: t('setting.filter_hashes_desc'),
          path: 'filter.hashes',
          defaultValue: false
        },
        {
          key: 'dexpub',
          name: t('setting.filter_dexpub_name'),
          description: t('setting.filter_dexpub_desc'),
          path: 'filter.dexpub',
          defaultValue: false
        },
        {
          key: 'modrinth',
          name: t('setting.filter_modrinth_name'),
          description: t('setting.filter_modrinth_desc'),
          path: 'filter.modrinth',
          defaultValue: false
        },
        {
          key: 'mixins',
          name: t('setting.filter_mixins_name'),
          description: t('setting.filter_mixins_desc'),
          path: 'filter.mixins',
          defaultValue: false
        }
      ]
    },
    {
      id: 'optimize',
      title: t('setting.category_optimize'),
      icon: '',
      bgColor: 'bg-lime-100',
      textColor: 'text-lime-800',
      hasMemory: true,
      hasPreview: true,
      items: [
        {
          key: 'optimizeEnabled',
          name: t('setting.optimize_enabled_name'),
          description: t('setting.optimize_enabled_desc'),
          path: 'optimization.enabled',
          defaultValue: true
        },
        {
          key: 'optimizeScript',
          name: t('setting.optimize_script_name'),
          description: t('setting.optimize_script_desc'),
          path: 'optimization.generateLaunchScript',
          defaultValue: true
        },
        {
          key: 'optimizeAikar',
          name: t('setting.optimize_aikar_name'),
          description: t('setting.optimize_aikar_desc'),
          path: 'optimization.aikarFlags',
          defaultValue: true
        },
        {
          key: 'optimizeStrip',
          name: t('setting.optimize_strip_name'),
          description: t('setting.optimize_strip_desc'),
          path: 'optimization.stripClientResources',
          defaultValue: true
        },
        {
          key: 'optimizeSensitive',
          name: t('setting.optimize_sensitive_name'),
          description: t('setting.optimize_sensitive_desc'),
          path: 'optimization.sanitizeSensitive',
          defaultValue: true
        },
        {
          key: 'optimizeRedundant',
          name: t('setting.optimize_redundant_name'),
          description: t('setting.optimize_redundant_desc'),
          path: 'optimization.detectRedundant',
          defaultValue: true
        },
        {
          key: 'optimizeProps',
          name: t('setting.optimize_props_name'),
          description: t('setting.optimize_props_desc'),
          path: 'optimization.generateServerProperties',
          defaultValue: true
        },
        {
          key: 'optimizeBackup',
          name: t('setting.optimize_backup_name'),
          description: t('setting.optimize_backup_desc'),
          path: 'optimization.backup',
          defaultValue: true
        },
        {
          key: 'optimizeDryRun',
          name: t('setting.optimize_dryrun_name'),
          description: t('setting.optimize_dryrun_desc'),
          path: 'optimization.dryRun',
          defaultValue: false
        }
      ]
    },
    {
      id: 'mirror',
      title: t('setting.category_mirror'),
      icon: '',
      bgColor: 'bg-cyan-100',
      textColor: 'text-cyan-800',
      items: [
        {
          key: 'mcimirror',
          name: t('setting.mirror_mcimirror_name'),
          description: t('setting.mirror_mcimirror_desc'),
          path: 'mirror.mcimirror',
          defaultValue: false
        },
        {
          key: 'mcimirrorModrinthOnly',
          name: t('setting.mirror_mcimirror_modrinth_only_name'),
          description: t('setting.mirror_mcimirror_modrinth_only_desc'),
          path: 'mirror.mcimirrorModrinthOnly',
          defaultValue: false
        },
        {
          key: 'bmclapi',
          name: t('setting.mirror_bmclapi_name'),
          description: t('setting.mirror_bmclapi_desc'),
          path: 'mirror.bmclapi',
          defaultValue: false
        }
      ]
    },
    {
      id: 'system',
      title: t('setting.category_system'),
      icon: '',
      bgColor: 'bg-purple-100',
      textColor: 'text-purple-800',
      hasLanguage: true,
      items: [
        {
          key: 'oaf',
          name: t('setting.system_oaf_name'),
          description: t('setting.system_oaf_desc'),
          path: 'oaf',
          defaultValue: false
        },
        {
          key: 'autoZip',
          name: t('setting.system_autozip_name'),
          description: t('setting.system_autozip_desc'),
          path: 'autoZip',
          defaultValue: false
        },
        {
          key: 'showAds',
          name: t('setting.system_show_ads_name'),
          description: t('setting.system_show_ads_desc'),
          path: 'showAds',
          defaultValue: true
        }
      ]
    }
  ];
});

const languageOptions = computed(() => {
  return [
    { label: '简体中文', value: 'zh_cn' },
    { label: '繁體中文（香港）', value: 'zh_hk' },
    { label: '繁體中文（台灣）', value: 'zh_tw' },
    { label: 'English', value: 'en_us' },
    { label: '日本語', value: 'ja_jp' },
    { label: 'Français', value: 'fr_fr' },
    { label: 'Deutsch', value: 'de_de' },
    { label: 'Español', value: 'es_es' }
  ];
});

function handleLanguageChange(value: Language) {
  setLanguage(value);
}

function handleShowAdsChange(value: boolean) {
  setShowAds(value);
}

const { t, locale } = useI18n();

function getConfigValue(path: string): boolean {
  const keys = path.split('.');
  let value: any = config.value;

  for (const key of keys) {
    value = value?.[key];
  }

  if (typeof value === 'boolean') {
    return value;
  }

  console.warn(`Config value at path "${path}" is not a boolean:`, value);
  return false;
}

function setConfigValue(path: string, newValue: boolean): void {
  const keys = path.split('.');
  let obj: any = config.value;

  for (let i = 0; i < keys.length - 1; i++) {
    if (obj[keys[i]] === undefined || obj[keys[i]] === null) {
      obj[keys[i]] = {};
    }
    obj = obj[keys[i]];
  }

  obj[keys[keys.length - 1]] = newValue;
}

// ------------------------------------------------------------------
// 内存与 JVM 参数
// ------------------------------------------------------------------

interface FlagAdjustment {
  flag: string;
  before: string | null;
  after: string;
  reason: string;
}

interface FlagsInfo {
  memoryMb: number;
  autoDetected: boolean;
  detectedMemoryMb: number;
  highMemoryProfile: boolean;
  arguments: string[];
  adjustments: FlagAdjustment[];
}

const flagsInfo = ref<FlagsInfo | null>(null);
const flagsLoading = ref(false);

async function loadFlags() {
  flagsLoading.value = true;
  try {
    const response = await axios.get('/optimize/flags');
    flagsInfo.value = response.data?.data ?? null;
  } catch (error) {
    console.error('读取 JVM 参数失败:', error);
  } finally {
    flagsLoading.value = false;
  }
}

/**
 * 内存以 MB 为单位编辑；0 表示自动探测。
 */
const memoryMb = computed({
  get: () => config.value.optimization?.memoryMb ?? 0,
  set: (value: number) => {
    if (!config.value.optimization) {
      config.value.optimization = createDefaultOptimization();
    }
    config.value.optimization.memoryMb = value;
  }
});

function handleMemoryChange(value: number | string) {
  const parsed = typeof value === 'number' ? value : parseInt(value, 10);
  memoryMb.value = Number.isFinite(parsed) ? Math.max(0, parsed) : 0;
  void loadFlags();
}

// ------------------------------------------------------------------
// dry-run 预览
// ------------------------------------------------------------------

const previewVisible = ref(false);
const previewPath = ref('');
const previewLoading = ref(false);
const previewReport = ref<any>(null);

function openPreview() {
  previewReport.value = null;
  previewVisible.value = true;
}

async function runPreview() {
  if (!previewPath.value.trim()) {
    message.warning(t('setting.optimize_preview_path_required'));
    return;
  }

  previewLoading.value = true;
  try {
    const response = await axios.post('/optimize/preview', {
      path: previewPath.value.trim(),
      memoryMb: memoryMb.value > 0 ? memoryMb.value : null
    });

    if (response.data?.status === 200) {
      previewReport.value = response.data.data;
      message.success(t('setting.optimize_preview_ok'));
    } else {
      message.error(response.data?.message || t('setting.optimize_preview_failed'));
    }
  } catch (error: any) {
    message.error(error?.response?.data?.message || error?.message || t('setting.optimize_preview_failed'));
  } finally {
    previewLoading.value = false;
  }
}

/** 把报告里的数组安全地转成展示用数组。 */
function asList(value: unknown): string[] {
  return Array.isArray(value) ? value.map((v) => String(v)) : [];
}

// ------------------------------------------------------------------

async function loadConfig() {
  try {
    const response = await axios.get('/config/get');
    const data = response.data ?? {};

    // 兼容旧配置：缺少 optimization 段落时补上默认值。
    if (!data.optimization) {
      data.optimization = createDefaultOptimization();
    } else {
      data.optimization = { ...createDefaultOptimization(), ...data.optimization };
    }

    config.value = data;
    console.log('[Setting] 配置已从后端刷新');
    void loadFlags();
  } catch (error) {
    console.error('加载配置失败:', error);
    message.error(t('setting.config_load_failed'));
  }
}

defineExpose({
  refreshConfig: loadConfig
});

async function saveConfig(newConfig: AppConfig) {
  try {
    await axios.post('/config/post', newConfig, {
      headers: { 'Content-Type': 'application/json' }
    });
    // 保存后刷新一次参数预览，保证与服务端一致。
    void loadFlags();
  } catch (error) {
    console.error('保存配置失败:', error);
    message.error(t('setting.config_save_failed'));
  }
}

onMounted(() => {
  loadConfig();
});

let isInitialLoad = true;
watch(config, (newValue) => {
  if (isInitialLoad) {
    isInitialLoad = false;
    return;
  }
  saveConfig(newValue);
}, { deep: true });
</script>

<template>
  <div class="tw:h-full tw:w-full tw:p-8 tw:overflow-auto tw:bg-gradient-to-br tw:from-slate-50 tw:via-blue-50 tw:to-indigo-50">
    <div class="tw:max-w-3xl tw:mx-auto">
      <div class="tw:mb-10 tw:animate-fade-in">
        <div class="tw:flex tw:items-center tw:gap-2 tw:mb-2">
          <span class="tw:text-xs tw:font-semibold tw:tracking-widest tw:uppercase tw:bg-gradient-to-r tw:from-emerald-500 tw:to-cyan-500 tw:bg-clip-text tw:text-transparent">
            {{ t('common.app_name') }}
          </span>
          <span class="tw:text-gray-300">/</span>
        </div>
        <h1 class="tw:text-3xl tw:font-bold tw:tracking-tight tw:text-gray-800">
          {{ t('menu.setting') }}
        </h1>
        <p class="tw:text-gray-500 tw:text-base tw:mt-2">{{ t('setting.subtitle') }}</p>
      </div>

      <div
        v-for="(category, index) in settings"
        :key="category.id"
        class="tw-bg-white tw:rounded-2xl tw:shadow-lg tw:p-7 tw:mb-6 tw:animate-fade-in-up tw:group tw:border tw:border-gray-100 tw:hover:tw:border-emerald-200 tw:transition-all duration-300"
        :style="{ animationDelay: `${index * 0.1}s` }"
      >
        <div class="tw:flex tw:items-center tw:justify-between tw:mb-6 tw:flex-wrap tw:gap-3">
          <h2 class="tw:text-xl tw:font-bold tw:text-gray-800 tw:flex tw:items-center tw:group-hover:tw:translate-x-2 tw:transition-transform duration-300">
            <span :class="[category.bgColor, category.textColor, 'tw-w-10 tw:h-10 tw:rounded-xl tw:flex tw:items-center tw:justify-center tw:mr-3 tw:shadow-md']">
              {{ category.icon }}
            </span>
            {{ category.title }}
          </h2>

          <a-button v-if="category.hasPreview" size="small" @click="openPreview">
            <template #icon><ExperimentOutlined /></template>
            {{ t('setting.optimize_preview_button') }}
          </a-button>
        </div>

        <!-- 内存分配 + JVM 参数预览 -->
        <div v-if="category.hasMemory" class="tw:mb-5 tw:p-4 tw:rounded-xl tw:border tw:border-amber-100 tw:bg-amber-50/40">
          <div class="tw:flex tw:items-center tw:justify-between tw:gap-4 tw:flex-wrap">
            <div class="tw:flex-1 tw:min-w-[220px]">
              <p class="tw:text-gray-700 tw:font-semibold tw:text-sm">{{ t('setting.optimize_memory_title') }}</p>
              <p class="tw:text-xs tw:text-gray-500 tw:mt-1">{{ t('setting.optimize_memory_desc') }}</p>
            </div>
            <div class="tw:flex tw:items-center tw:gap-2">
              <a-input-number
                :value="memoryMb"
                :min="0"
                :max="32768"
                :step="512"
                :placeholder="t('setting.optimize_memory_auto')"
                class="tw:w-32"
                @change="handleMemoryChange"
              />
              <span class="tw:text-xs tw:text-gray-500">MB</span>
            </div>
          </div>

          <div class="tw:flex tw:items-center tw:gap-2 tw:mt-3 tw:flex-wrap">
            <a-tag v-if="flagsInfo" :color="flagsInfo.autoDetected ? 'blue' : 'green'">
              {{ t('setting.optimize_memory_current', {
                memory: flagsInfo.memoryMb,
                auto: flagsInfo.autoDetected ? ` (${t('setting.optimize_memory_auto')})` : ''
              }) }}
            </a-tag>
            <a-tag v-if="flagsInfo?.highMemoryProfile" color="orange">
              {{ t('setting.optimize_flags_high_memory') }}
            </a-tag>
            <span v-if="flagsLoading" class="tw:text-xs tw:text-gray-400">{{ t('setting.optimize_flags_loading') }}</span>
          </div>

          <a-collapse v-if="flagsInfo" ghost size="small" class="tw:mt-2 tw:flags-collapse">
            <a-collapse-panel key="flags" :header="t('setting.optimize_flags_preview')">
              <pre class="tw:text-[11px] tw:whitespace-pre-wrap tw:break-all tw:text-gray-600 tw:bg-white tw:rounded-lg tw:p-3 tw:border tw:border-gray-100 tw:m-0">{{ flagsInfo.arguments.join(' ') }}</pre>

              <a-table
                :data-source="flagsInfo.adjustments"
                :columns="[
                  { title: 'Flag', dataIndex: 'flag', key: 'flag', width: 240 },
                  { title: '→', dataIndex: 'after', key: 'after', width: 80 },
                  { title: '', dataIndex: 'reason', key: 'reason' }
                ]"
                :pagination="false"
                size="small"
                row-key="flag"
                class="tw:mt-3"
              />
            </a-collapse-panel>
          </a-collapse>
        </div>

        <div class="tw:grid tw:grid-cols-1 md:tw:grid-cols-2 lg:tw:grid-cols-3 tw:gap-4">
          <div
            v-if="category.hasLanguage"
            class="tw:flex tw:flex-col tw:justify-between tw:p-4 tw:border tw:border-gray-100 tw:rounded-xl tw-hover:bg-gradient-to-r tw:hover:from-emerald-50 tw:hover:to-cyan-50 tw:hover:border-emerald-200 tw:transition-all duration-300 tw:group/item"
          >
            <div class="tw:flex-1">
              <p class="tw:text-gray-700 tw:font-semibold tw:text-sm tw:group-hover/item:tw:text-emerald-700 tw:transition-colors tw:flex tw:items-center tw:gap-2">
                {{ t('setting.language_title') }}
              </p>
              <p class="tw:text-xs tw:text-gray-500 tw:mt-2">{{ t('setting.language_desc') }}</p>
              <div class="tw-mt-3">
                <a-select
                  :value="locale"
                  :options="languageOptions"
                  @change="handleLanguageChange"
                  class="tw:w-full"
                />
              </div>
            </div>
          </div>
          <div
            v-for="item in category.items"
            :key="item.key"
            class="tw:flex tw:items-center tw:justify-between tw:p-4 tw:border tw:border-gray-100 tw:rounded-xl tw-hover:bg-gradient-to-r tw:hover:from-emerald-50 tw:hover:to-cyan-50 tw:hover:border-emerald-200 tw:transition-all duration-300 tw:group/item"
          >
            <div class="tw:flex-1">
              <p class="tw:text-gray-700 tw:font-semibold tw:text-sm tw:group-hover/item:tw:text-emerald-700 tw:transition-colors">{{ item.name }}</p>
              <p class="tw:text-xs tw:text-gray-500 tw:mt-1">{{ item.description }}</p>
            </div>
            <a-switch
              v-if="item.key !== 'showAds'"
              :checked="getConfigValue(item.path)"
              @change="setConfigValue(item.path, $event)"
              :checked-children="t('setting.switch_on')"
              :un-checked-children="t('setting.switch_off')"
            />
            <a-switch
              v-if="item.key === 'showAds'"
              :checked="showAds"
              @change="handleShowAdsChange"
              :checked-children="t('setting.switch_on')"
              :un-checked-children="t('setting.switch_off')"
            />
          </div>
        </div>
      </div>

      <!-- Java 检测（原「我的服务端」页面迁移而来） -->
      <JavaPanel />

      <!-- 日志 -->
      <LogsPanel />

      <!-- dry-run 预览弹窗 -->
      <a-modal
        v-model:open="previewVisible"
        :title="t('setting.optimize_preview_title')"
        :footer="null"
        width="760px"
      >
        <p class="tw:text-xs tw:text-gray-500 tw:mb-3">{{ t('setting.optimize_preview_desc') }}</p>

        <a-space class="tw:w-full tw:mb-4">
          <a-input
            v-model:value="previewPath"
            :placeholder="t('setting.optimize_preview_path_placeholder')"
            allow-clear
            class="tw:w-[420px]"
            @press-enter="runPreview"
          />
          <a-button type="primary" :loading="previewLoading" @click="runPreview">
            {{ t('setting.optimize_preview_button') }}
          </a-button>
        </a-space>

        <a-spin :spinning="previewLoading">
          <div v-if="previewReport" class="tw:max-h-[460px] tw:overflow-auto tw:pr-1">
            <div class="tw:flex tw:items-center tw:gap-2 tw:flex-wrap tw:mb-3">
              <a-tag color="blue">{{ t('setting.optimize_report_dryrun_badge') }}</a-tag>
              <a-tag>{{ t('setting.optimize_report_memory') }}: {{ previewReport.memoryMb }} MB</a-tag>
              <a-tag v-if="previewReport.highMemoryProfile" color="orange">&gt;12GB</a-tag>
              <a-tag>{{ previewReport.elapsedMs }} ms</a-tag>
            </div>

            <a-descriptions
              bordered
              size="small"
              :column="1"
              class="tw:mb-3"
            >
              <a-descriptions-item :label="t('setting.optimize_report_generated_scripts')">
                {{ asList(previewReport.generatedScripts).join(', ') || t('setting.optimize_report_none') }}
              </a-descriptions-item>
              <a-descriptions-item :label="t('setting.optimize_report_removed')">
                {{ previewReport.removedClientResources?.length || 0 }}
              </a-descriptions-item>
              <a-descriptions-item :label="t('setting.optimize_report_sensitive')">
                {{ previewReport.sanitizedSensitiveFiles?.length || 0 }}
              </a-descriptions-item>
              <a-descriptions-item :label="t('setting.optimize_report_redundancy')">
                {{ previewReport.redundancies?.length || 0 }}
              </a-descriptions-item>
              <a-descriptions-item
                v-if="previewReport.backupDirectory"
                :label="t('setting.optimize_report_backup')"
              >
                <span class="tw:break-all tw:text-xs">{{ previewReport.backupDirectory }}</span>
              </a-descriptions-item>
            </a-descriptions>

            <template v-for="(group, gIndex) in [
              { title: t('setting.optimize_report_gains'), key: 'expectedGains', color: 'green' },
              { title: t('setting.optimize_report_suggestions'), key: 'suggestions', color: 'blue' },
              { title: t('setting.optimize_report_warnings'), key: 'warnings', color: 'orange' }
            ]" :key="group.key">
              <div v-if="asList(previewReport[group.key]).length > 0" class="tw:mb-3">
                <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ group.title }}</p>
                <ul class="tw:list-disc tw:pl-5 tw:text-xs tw:text-gray-600 tw:space-y-1">
                  <li v-for="(text, i) in asList(previewReport[group.key])" :key="`${gIndex}-${i}`">{{ text }}</li>
                </ul>
              </div>
            </template>

            <div v-if="previewReport.configChanges?.length" class="tw:mb-3">
              <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ t('setting.optimize_report_config_changes') }}</p>
              <a-table
                :data-source="previewReport.configChanges"
                :columns="[
                  { title: 'Key', dataIndex: 'key', key: 'key', width: 220 },
                  { title: '→', dataIndex: 'after', key: 'after', width: 100 },
                  { title: '', dataIndex: 'reason', key: 'reason' }
                ]"
                :pagination="false"
                size="small"
                row-key="key"
              />
            </div>
          </div>
        </a-spin>
      </a-modal>
    </div>
  </div>
</template>

<style scoped>
@keyframes fadeIn {
    from {
        opacity: 0;
    }
    to {
        opacity: 1;
    }
}

@keyframes fadeInUp {
    from {
        opacity: 0;
        transform: translateY(30px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

.tw-animate-fade-in {
    animation: fadeIn 0.6s ease-out;
}

.tw-animate-fade-in-up {
    animation: fadeInUp 0.6s ease-out;
    animation-fill-mode: both;
}

/* 让 JVM 参数的折叠面板更紧凑。 */
.tw\:flags-collapse :deep(.ant-collapse-header) {
    padding: 6px 0 !important;
    font-size: 12px;
    color: #6b7280;
}

.tw\:flags-collapse :deep(.ant-collapse-content-box) {
    padding: 0 !important;
}
</style>
