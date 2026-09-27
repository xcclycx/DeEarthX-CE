import { ref, computed } from 'vue';
import { get, postJson, describeError } from '../utils/api';

/**
 * 一条程序错误 / 警告记录。
 */
export interface ProcessError {
  id: string;
  timestamp: string;
  level: string;
  message: string;
  detail?: string | null;
  source?: string | null;
  acknowledged: boolean;
  /** 是否来自后端环形缓冲（后端记录可被确认），本地记录只存在于本次会话。 */
  remote: boolean;
}

const ERRORS_PATH = '/errors/recent';

/** 所有记录，最新在前。 */
const errors = ref<ProcessError[]>([]);

/** 是否正在拉取。 */
const loading = ref(false);

/** 最近一次拉取失败的时间，避免反复弹同样的提示。 */
let lastFetchFailureAt = 0;

/** 本地记录的自增序号。 */
let localSeq = 0;

/** 后端返回的未读数量。 */
const remoteUnacknowledged = ref(0);

/**
 * 去重窗口：相同消息在 5 秒内只记录一次，
 * 避免一个失败请求触发多次重试时把提示刷屏。
 */
const recentFingerprints = new Map<string, number>();
const DEDUPE_WINDOW_MS = 5000;

function fingerprint(level: string, message: string): string {
  return `${level}::${message}`;
}

function shouldSkipDuplicate(level: string, message: string): boolean {
  const key = fingerprint(level, message);
  const now = Date.now();
  const last = recentFingerprints.get(key);

  // 顺手清理过期条目，避免 Map 无限增长。
  if (recentFingerprints.size > 200) {
    for (const [k, ts] of recentFingerprints) {
      if (now - ts > DEDUPE_WINDOW_MS) {
        recentFingerprints.delete(k);
      }
    }
  }

  if (last !== undefined && now - last < DEDUPE_WINDOW_MS) {
    return true;
  }

  recentFingerprints.set(key, now);
  return false;
}

const MAX_LOCAL_ERRORS = 200;

/**
 * 记录一条本地错误（前端捕获到的异常）。
 */
export function pushLocalError(
  message: string,
  options: { level?: string; detail?: string; source?: string; silent?: boolean } = {}
): ProcessError | null {
  const level = options.level || 'error';
  const text = (message || '').trim() || '未知错误';

  if (shouldSkipDuplicate(level, text)) {
    return null;
  }

  const record: ProcessError = {
    id: `local-${Date.now()}-${localSeq++}`,
    timestamp: new Date().toISOString(),
    level,
    message: text,
    detail: options.detail ?? null,
    source: options.source ?? 'frontend',
    acknowledged: false,
    remote: false
  };

  errors.value = [record, ...errors.value].slice(0, MAX_LOCAL_ERRORS + 100);
  return record;
}

/** 便利方法：记录一条警告。 */
export function pushLocalWarning(message: string, detail?: string, source = 'frontend'): ProcessError | null {
  return pushLocalError(message, { level: 'warn', detail, source });
}

/**
 * 从后端拉取最近的错误与警告。失败时不抛错、也不弹提示
 * （否则后端刚启动的那一刻会弹出自己连不上自己的循环提示）。
 */
export async function fetchRemoteErrors(options: { silent?: boolean } = {}): Promise<boolean> {
  if (loading.value) return false;
  loading.value = true;

  try {
    const response = await get<{
      status: number;
      data: { errors: Array<Omit<ProcessError, 'remote'>>; unacknowledged: number };
    }>(ERRORS_PATH, 8000);

    const remoteList = response?.data?.errors ?? [];
    remoteUnacknowledged.value = response?.data?.unacknowledged ?? 0;

    // 本地记录保留在后端记录前面（本地记录通常更新）。
    const localOnly = errors.value.filter((e) => !e.remote);
    const remote = remoteList.map<ProcessError>((item) => ({ ...item, remote: true }));

    errors.value = [...localOnly, ...remote];
    return true;
  } catch (error) {
    const now = Date.now();
    if (!options.silent && now - lastFetchFailureAt > 60_000) {
      lastFetchFailureAt = now;
      pushLocalWarning('无法读取后端错误记录', describeError(error), 'error-store');
    }
    return false;
  } finally {
    loading.value = false;
  }
}

/**
 * 标记为已读。本地记录直接改标记，后端记录调用接口。
 */
export async function acknowledgeErrors(ids?: string[]): Promise<number> {
  const targetIds = ids ?? errors.value.filter((e) => !e.acknowledged).map((e) => e.id);
  const remoteIds = errors.value
    .filter((e) => e.remote && targetIds.includes(e.id))
    .map((e) => e.id);

  for (const record of errors.value) {
    if (targetIds.includes(record.id)) {
      record.acknowledged = true;
    }
  }

  if (remoteIds.length > 0) {
    try {
      await postJson('/errors/ack', { ids: remoteIds }, 8000);
      remoteUnacknowledged.value = Math.max(0, remoteUnacknowledged.value - remoteIds.length);
    } catch {
      // 确认失败不影响本地标记，下次拉取会恢复。
    }
  }

  return targetIds.length;
}

/** 清空全部错误记录（含后端缓冲）。 */
export async function clearErrors(): Promise<boolean> {
  errors.value = [];
  remoteUnacknowledged.value = 0;
  recentFingerprints.clear();

  try {
    await postJson('/errors/clear', undefined, 8000);
    return true;
  } catch {
    return false;
  }
}

/** 未读错误数量（本地 + 后端）。 */
export const unreadErrorCount = computed(
  () => errors.value.filter((e) => !e.acknowledged).length
);

/** 未读数（原始 ref，供非组件环境读取）。 */
export function getUnreadCount(): number {
  return errors.value.filter((e) => !e.acknowledged).length;
}

export { errors, loading as errorsLoading, remoteUnacknowledged };
