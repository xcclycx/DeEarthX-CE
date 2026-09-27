<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { message, Modal } from 'ant-design-vue';
import {
  ReloadOutlined, DownloadOutlined, DeleteOutlined, FolderOpenOutlined,
  SearchOutlined, FileTextOutlined
} from '@ant-design/icons-vue';
import { get, postJson, downloadFile, formatBytes } from '../utils/api';

const { t } = useI18n();

interface LogFileInfo {
  name: string;
  sizeBytes: number;
  lastModified: string;
  lineCount: number;
}

interface LogLine {
  lineNumber: number;
  level: string;
  timestamp: string;
  message: string;
  file: string;
}

const files = ref<LogFileInfo[]>([]);
const lines = ref<LogLine[]>([]);
const directory = ref('');
const totalMatched = ref(0);
const truncated = ref(false);
const scannedFiles = ref(0);

const loading = ref(false);
const exporting = ref(false);

/** 一次最多拉取的行数。 */
const maxLines = 500;

const selectedFile = ref<string>('');
const selectedLevel = ref<string>('all');
const keyword = ref('');

/** 关键字输入的防抖句柄。 */
let keywordTimer: ReturnType<typeof setTimeout> | null = null;

const levelOptions = computed(() => [
  { label: t('setting.logs_level_all'), value: 'all' },
  { label: t('setting.logs_level_debug'), value: 'debug' },
  { label: t('setting.logs_level_info'), value: 'info' },
  { label: t('setting.logs_level_warn'), value: 'warn' },
  { label: t('setting.logs_level_error'), value: 'error' }
]);

const fileOptions = computed(() => [
  { label: t('setting.logs_all_files'), value: '' },
  ...files.value.map((f) => ({
    label: `${f.name}（${formatBytes(f.sizeBytes)}）`,
    value: f.name
  }))
]);

function buildQuery(extra: Record<string, string> = {}): string {
  const params = new URLSearchParams();
  if (selectedFile.value) params.set('file', selectedFile.value);
  if (selectedLevel.value && selectedLevel.value !== 'all') params.set('level', selectedLevel.value);
  if (keyword.value.trim()) params.set('keyword', keyword.value.trim());
  for (const [key, value] of Object.entries(extra)) {
    params.set(key, value);
  }
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

async function loadLogs() {
  loading.value = true;
  try {
    const response = await get<{
      status: number;
      data: {
        files: LogFileInfo[];
        directory: string;
        lines: LogLine[];
        totalMatched: number;
        scannedFiles: number;
        truncated: boolean;
      };
    }>(`/logs${buildQuery({ maxLines: String(maxLines) })}`, 20000);

    files.value = response.data.files || [];
    lines.value = response.data.lines || [];
    directory.value = response.data.directory || '';
    totalMatched.value = response.data.totalMatched || 0;
    scannedFiles.value = response.data.scannedFiles || 0;
    truncated.value = !!response.data.truncated;

    // 首次加载时默认选中最新日志文件，让文件下拉框有明确的选择。
    if (!selectedFile.value && files.value.length > 0) {
      selectedFile.value = '';
    }
  } catch (error: any) {
    message.error(error?.message || t('setting.logs_load_failed'));
  } finally {
    loading.value = false;
  }
}

function handleKeywordInput() {
  if (keywordTimer !== null) {
    clearTimeout(keywordTimer);
  }
  // 防抖，避免每敲一个字符就请求一次后端。
  keywordTimer = setTimeout(() => {
    keywordTimer = null;
    void loadLogs();
  }, 400);
}

async function exportLogs(all: boolean) {
  exporting.value = true;
  try {
    const path = all
      ? '/logs/export?all=true'
      : `/logs/export${buildQuery()}`;

    await downloadFile(path, 'deearthx-logs.txt');
    message.success(all ? t('setting.logs_export_all_success') : t('setting.logs_export_filtered_success'));
  } catch (error: any) {
    message.error(error?.message || t('setting.logs_export_failed'));
  } finally {
    exporting.value = false;
  }
}

function confirmClear(all: boolean) {
  const run = async () => {
    try {
      const response = await postJson<{ status: number; message: string; data: { removed: number } }>(
        `/logs/cleanup?all=${all ? 'true' : 'false'}`,
        undefined,
        15000
      );
      message.success(t('setting.logs_cleared', { count: response.data?.removed ?? 0 }));
      await loadLogs();
    } catch (error: any) {
      message.error(error?.message || t('setting.logs_clear_failed'));
    }
  };

  if (!all) {
    void run();
    return;
  }

  Modal.confirm({
    title: t('setting.logs_clear_all'),
    content: t('setting.logs_clear_confirm'),
    okText: t('common.confirm'),
    cancelText: t('common.cancel'),
    okType: 'danger',
    onOk: run
  });
}

async function openFolder() {
  try {
    await get('/logs/folder', 8000);
    message.success(t('setting.logs_open_folder_success'));
  } catch (error: any) {
    message.error(error?.message || t('setting.logs_open_folder_failed'));
  }
}

function levelClass(level: string): string {
  switch (level) {
    case 'error':
      return 'tw:text-red-600';
    case 'warn':
      return 'tw:text-amber-600';
    case 'debug':
      return 'tw:text-gray-400';
    default:
      return 'tw:text-emerald-600';
  }
}

const displayCount = computed(() => lines.value.length);

onMounted(() => {
  void loadLogs();
});

defineExpose({ refresh: loadLogs });
</script>

<template>
  <div class="tw-bg-white tw:rounded-2xl tw:shadow-lg tw:p-7 tw:mb-6 tw:border tw:border-gray-100 tw:transition-all duration-300">
    <div class="tw:flex tw:items-center tw:justify-between tw:mb-6 tw:flex-wrap tw:gap-3">
      <h2 class="tw:text-xl tw:font-bold tw:text-gray-800 tw:flex tw:items-center">
        <span class="tw-w-10 tw:h-10 tw:rounded-xl tw:flex tw:items-center tw:justify-center tw:mr-3 tw:shadow-md tw:bg-slate-100 tw:text-slate-700">
          <FileTextOutlined />
        </span>
        {{ t('setting.logs_title') }}
      </h2>

      <a-space :size="8" wrap>
        <a-button size="small" :loading="loading" @click="loadLogs">
          <template #icon><ReloadOutlined /></template>
          {{ t('setting.logs_refresh') }}
        </a-button>
        <a-button size="small" :loading="exporting" @click="exportLogs(false)">
          <template #icon><DownloadOutlined /></template>
          {{ t('setting.logs_export_filtered') }}
        </a-button>
        <a-button size="small" :loading="exporting" @click="exportLogs(true)">
          <template #icon><DownloadOutlined /></template>
          {{ t('setting.logs_export_all') }}
        </a-button>
        <a-button size="small" @click="openFolder">
          <template #icon><FolderOpenOutlined /></template>
          {{ t('setting.logs_open_folder') }}
        </a-button>
        <a-dropdown>
          <a-button size="small" danger>
            <template #icon><DeleteOutlined /></template>
            {{ t('setting.logs_clear') }}
          </a-button>
          <template #overlay>
            <a-menu>
              <a-menu-item key="retention" @click="confirmClear(false)">
                {{ t('setting.logs_clear_retention') }}
              </a-menu-item>
              <a-menu-item key="all" danger @click="confirmClear(true)">
                {{ t('setting.logs_clear_all') }}
              </a-menu-item>
            </a-menu>
          </template>
        </a-dropdown>
      </a-space>
    </div>

    <p class="tw:text-xs tw:text-gray-500 tw:mb-4">{{ t('setting.logs_desc') }}</p>

    <!-- 筛选区 -->
    <div class="tw:grid tw:grid-cols-1 md:tw:grid-cols-3 tw:gap-3 tw:mb-4">
      <div>
        <p class="tw:text-xs tw:text-gray-500 tw:mb-1">{{ t('setting.logs_file') }}</p>
        <a-select
          v-model:value="selectedFile"
          :options="fileOptions"
          class="tw:w-full"
          size="small"
          @change="loadLogs"
        />
      </div>
      <div>
        <p class="tw:text-xs tw:text-gray-500 tw:mb-1">{{ t('setting.logs_level') }}</p>
        <a-select
          v-model:value="selectedLevel"
          :options="levelOptions"
          class="tw:w-full"
          size="small"
          @change="loadLogs"
        />
      </div>
      <div>
        <p class="tw:text-xs tw:text-gray-500 tw:mb-1">{{ t('setting.logs_keyword') }}</p>
        <a-input
          v-model:value="keyword"
          size="small"
          allow-clear
          :placeholder="t('setting.logs_keyword_placeholder')"
          @input="handleKeywordInput"
          @press-enter="loadLogs"
        >
          <template #prefix><SearchOutlined class="tw:text-gray-400" /></template>
        </a-input>
      </div>
    </div>

    <!-- 统计信息 -->
    <div class="tw:flex tw:items-center tw:justify-between tw:text-[11px] tw:text-gray-400 tw:mb-2 tw:flex-wrap tw:gap-2">
      <span>{{ t('setting.logs_line_count', { shown: displayCount, total: totalMatched }) }}</span>
      <span v-if="scannedFiles > 1">{{ t('setting.logs_scanned_files', { count: scannedFiles }) }}</span>
    </div>

    <a-alert
      v-if="truncated"
      type="info"
      show-icon
      :message="t('setting.logs_truncated', { max: maxLines })"
      class="tw:mb-3"
    />

    <!-- 日志内容 -->
    <a-spin :spinning="loading && lines.length === 0">
      <a-empty v-if="lines.length === 0" :description="t('setting.logs_empty')">
        <template #description>
          <div class="tw:flex tw:flex-col tw:items-center">
            <span class="tw:text-gray-500">{{ t('setting.logs_empty') }}</span>
            <span class="tw:text-xs tw:text-gray-400 tw:mt-1">{{ t('setting.logs_empty_hint') }}</span>
          </div>
        </template>
      </a-empty>

      <div
        v-else
        class="tw:max-h-[420px] tw:overflow-auto tw:rounded-xl tw:border tw:border-gray-100 tw:bg-slate-50 tw:p-3 tw:font-mono tw:text-[11px] tw:leading-relaxed"
      >
        <div
          v-for="line in lines"
          :key="`${line.file}-${line.lineNumber}`"
          class="tw:flex tw:gap-2 tw:py-[1px] hover:tw:bg-white tw:rounded tw:px-1"
        >
          <span class="tw:text-gray-400 tw:shrink-0">{{ line.timestamp }}</span>
          <span class="tw:shrink-0 tw:uppercase tw:font-semibold" :class="levelClass(line.level)">
            [{{ line.level }}]
          </span>
          <span class="tw:text-gray-700 tw:break-all">{{ line.message }}</span>
        </div>
      </div>
    </a-spin>

    <p v-if="directory" class="tw:text-[11px] tw:text-gray-400 tw:mt-3 tw:break-all">
      {{ t('setting.logs_directory') }}: {{ directory }}
    </p>
  </div>
</template>
