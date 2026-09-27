<script lang="ts" setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Modal } from 'ant-design-vue';
import { BellOutlined, CheckOutlined, DeleteOutlined, ReloadOutlined } from '@ant-design/icons-vue';
import {
  errors,
  errorsLoading,
  unreadErrorCount,
  fetchRemoteErrors,
  acknowledgeErrors,
  clearErrors,
  pushLocalError,
  type ProcessError
} from '../stores/errorStore';

const { t } = useI18n();

const visible = ref(false);

/** 轮询间隔。错误提醒需要及时，但也不能太频繁影响性能。 */
const POLL_INTERVAL_MS = 30000;
let pollTimer: ReturnType<typeof setInterval> | null = null;

/** 已经提示过的记录 id，避免每次轮询重复弹提示。 */
const notifiedIds = new Set<string>();

/**
 * 新错误提示。使用 Ant Design 的 message 而不是 notification，
 * 因为标题栏空间有限，notification 会遮挡内容区。
 */
async function showToastFor(record: ProcessError) {
  if (notifiedIds.has(record.id)) return;
  notifiedIds.add(record.id);

  const isWarn = record.level === 'warn';

  // 动态 import 避免模块初始化顺序问题。
  const { message } = await import('ant-design-vue');
  const text = record.message.length > 80 ? `${record.message.slice(0, 80)}…` : record.message;

  if (isWarn) {
    message.warning(text, 6);
  } else {
    message.error(text, 8);
  }
}

/** 处理新出现的记录：只提示未读且是本次会话新增的。 */
function handleNewRecords(list: ProcessError[]) {
  for (const record of list) {
    if (!record.acknowledged) {
      void showToastFor(record);
    } else {
      // 已读记录直接记入已提示集合，避免用户点已读后又弹一次。
      notifiedIds.add(record.id);
    }
  }
}

async function refresh(options: { silent?: boolean } = {}) {
  await fetchRemoteErrors(options);
  handleNewRecords(errors.value);
}

async function markAllRead() {
  const count = await acknowledgeErrors();
  if (count > 0) {
    const { message } = await import('ant-design-vue');
    message.success(t('setting.errors_ack_success', { count }));
  }
}

function confirmClear() {
  Modal.confirm({
    title: t('setting.errors_clear'),
    content: t('setting.logs_clear_confirm'),
    okText: t('common.confirm'),
    cancelText: t('common.cancel'),
    okType: 'danger',
    onOk: async () => {
      await clearErrors();
      const { message } = await import('ant-design-vue');
      message.success(t('setting.errors_clear_success'));
    }
  });
}

function levelColor(level: string): string {
  switch (level) {
    case 'error':
      return 'tw:text-red-600 tw:bg-red-50 tw:border-red-200';
    case 'warn':
      return 'tw:text-amber-600 tw:bg-amber-50 tw:border-amber-200';
    case 'debug':
      return 'tw:text-gray-500 tw:bg-gray-50 tw:border-gray-200';
    default:
      return 'tw:text-emerald-600 tw:bg-emerald-50 tw:border-emerald-200';
  }
}

function levelLabel(level: string): string {
  switch (level) {
    case 'error':
      return t('setting.logs_level_error');
    case 'warn':
      return t('setting.logs_level_warn');
    case 'debug':
      return t('setting.logs_level_debug');
    default:
      return t('setting.logs_level_info');
  }
}

function formatTime(timestamp: string): string {
  try {
    const date = new Date(timestamp);
    return date.toLocaleString();
  } catch {
    return timestamp;
  }
}

const sortedErrors = computed(() =>
  [...errors.value].sort((a, b) => (a.timestamp < b.timestamp ? 1 : -1))
);

const hasUnread = computed(() => unreadErrorCount.value > 0);

onMounted(() => {
  // 首次拉取稍作延迟，避开后端启动阶段。
  setTimeout(() => void refresh(), 4000);
  pollTimer = setInterval(() => void refresh({ silent: true }), POLL_INTERVAL_MS);
});

onUnmounted(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
});

// 打开抽屉时刷新一次，保证内容是最新的。
watch(visible, (open) => {
  if (open) {
    void refresh({ silent: true });
  }
});

defineExpose({ refresh, pushLocalError });
</script>

<template>
  <div class="tw:relative tw:flex tw:items-center">
    <a-tooltip :title="t('setting.errors_bell_title')">
      <a-badge :count="unreadErrorCount" :offset="[-4, 4]" size="small">
        <button
          type="button"
          class="tw:flex tw:items-center tw:justify-center tw:w-8 tw:h-8 tw:rounded-lg tw:border tw:transition-all tw:cursor-pointer"
          :class="hasUnread
            ? 'tw:border-red-200 tw:bg-red-50 tw:text-red-500 hover:tw:bg-red-100'
            : 'tw:border-gray-200 tw:bg-white tw:text-gray-500 hover:tw:bg-gray-50'"
          @mousedown.stop
          @click="visible = true"
        >
          <BellOutlined class="tw:text-base" />
        </button>
      </a-badge>
    </a-tooltip>

    <a-drawer
      v-model:open="visible"
      :title="t('setting.errors_title')"
      placement="right"
      :width="520"
    >
      <template #extra>
        <a-space>
          <a-button size="small" @click="refresh()" :loading="errorsLoading">
            <template #icon><ReloadOutlined /></template>
          </a-button>
          <a-button size="small" :disabled="unreadErrorCount === 0" @click="markAllRead">
            <template #icon><CheckOutlined /></template>
            {{ t('setting.errors_ack') }}
          </a-button>
          <a-button size="small" danger :disabled="errors.length === 0" @click="confirmClear">
            <template #icon><DeleteOutlined /></template>
          </a-button>
        </a-space>
      </template>

      <p class="tw:text-xs tw:text-gray-500 tw:mb-4">{{ t('setting.errors_desc') }}</p>

      <a-spin :spinning="errorsLoading && errors.length === 0">
        <a-empty v-if="sortedErrors.length === 0" :description="t('setting.errors_empty')" />

        <div v-else class="tw:flex tw:flex-col tw:gap-3">
          <div
            v-for="record in sortedErrors"
            :key="record.id"
            class="tw:rounded-xl tw:border tw:p-3 tw:transition-all"
            :class="[
              levelColor(record.level),
              record.acknowledged ? 'tw:opacity-60' : ''
            ]"
          >
            <div class="tw:flex tw:items-start tw:justify-between tw:gap-2">
              <span class="tw:text-[11px] tw:font-semibold tw:uppercase tw:tracking-wide">
                {{ levelLabel(record.level) }}
              </span>
              <span class="tw:text-[11px] tw:text-gray-500 tw:whitespace-nowrap">
                {{ formatTime(record.timestamp) }}
              </span>
            </div>

            <p class="tw:text-sm tw:mt-1 tw:break-words tw:text-gray-800">{{ record.message }}</p>

            <a-collapse
              v-if="record.detail"
              ghost
              size="small"
              class="tw:mt-1 tw:error-detail-collapse"
            >
              <a-collapse-panel key="d" :header="t('setting.errors_detail')">
                <pre class="tw:text-[11px] tw:whitespace-pre-wrap tw:break-all tw:text-gray-600 tw:m-0">{{ record.detail }}</pre>
              </a-collapse-panel>
            </a-collapse>

            <div class="tw:flex tw:items-center tw:justify-between tw:mt-2">
              <span v-if="record.source" class="tw:text-[11px] tw:text-gray-400">{{ record.source }}</span>
              <a-button
                v-if="!record.acknowledged"
                type="link"
                size="small"
                class="tw:px-0"
                @click="acknowledgeErrors([record.id])"
              >
                {{ t('setting.errors_ack') }}
              </a-button>
            </div>
          </div>
        </div>
      </a-spin>
    </a-drawer>
  </div>
</template>

<style scoped>
/* 让详情折叠面板更紧凑，避免抽屉里出现大片留白。 */
.tw\:error-detail-collapse :deep(.ant-collapse-header) {
  padding: 4px 0 !important;
  font-size: 12px;
  color: #6b7280;
}

.tw\:error-detail-collapse :deep(.ant-collapse-content-box) {
  padding: 4px 0 !important;
}
</style>
