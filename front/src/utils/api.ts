/**
 * 后端 API 基础地址与统一请求封装。
 *
 * 统一封装的目的：把「请求失败」收敛到一处，便于程序错误提醒统一展示，
 * 同时避免每个视图各写一遍 fetch + 错误处理。
 */

export const API_HOST: string = (import.meta.env.VITE_API_HOST as string) || 'localhost';
export const API_PORT: string = (import.meta.env.VITE_API_PORT as string) || '37019';
export const API_BASE = `http://${API_HOST}:${API_PORT}`;

export class ApiError extends Error {
  readonly status: number | null;
  readonly url: string;

  constructor(message: string, url: string, status: number | null = null) {
    super(message);
    this.name = 'ApiError';
    this.url = url;
    this.status = status;
  }
}

function describeStatus(status: number): string {
  if (status === 404) return '接口不存在（404）';
  if (status === 500) return '后端内部错误（500）';
  if (status === 503) return '后端服务不可用（503）';
  if (status >= 500) return `后端错误（${status}）`;
  if (status >= 400) return `请求被拒绝（${status}）`;
  return `请求失败（${status}）`;
}

/** 构造完整 URL。 */
export function apiUrl(path: string): string {
  return path.startsWith('http') ? path : `${API_BASE}${path.startsWith('/') ? '' : '/'}${path}`;
}

/** 从任意错误对象里提取一条可读信息。 */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }
  if (error instanceof DOMException && error.name === 'AbortError') {
    return '请求已取消';
  }
  if (error instanceof TypeError) {
    return '无法连接到后端服务，请确认 DeEarthX.Core 正在运行';
  }
  if (error instanceof Error) {
    return error.message || '未知错误';
  }
  if (typeof error === 'string') {
    return error;
  }
  return '未知错误';
}

/**
 * 发起请求并返回解析后的 JSON。
 * 失败时抛出 <see cref="ApiError"/>，调用方可以决定是否提示用户。
 */
export async function request<T = any>(
  path: string,
  init: RequestInit = {},
  timeoutMs = 15000
): Promise<T> {
  const url = apiUrl(path);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  // 允许外部传入 signal 时以外部为准。
  const signal = init.signal ?? controller.signal;

  try {
    const response = await fetch(url, { ...init, signal });

    if (!response.ok) {
      let detail = '';
      try {
        const text = await response.text();
        if (text) {
          try {
            const parsed = JSON.parse(text);
            detail = parsed?.message || '';
          } catch {
            detail = text.slice(0, 200);
          }
        }
      } catch {
        // 读取响应体失败时忽略。
      }

      throw new ApiError(
        detail ? `${describeStatus(response.status)}: ${detail}` : describeStatus(response.status),
        url,
        response.status
      );
    }

    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      return (await response.json()) as T;
    }

    return (await response.text()) as unknown as T;
  } catch (error) {
    if (error instanceof ApiError) {
      throw error;
    }
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw new ApiError('请求超时，后端服务可能无响应', url, null);
    }
    throw new ApiError(describeError(error), url, null);
  } finally {
    clearTimeout(timer);
  }
}

/** GET 请求。 */
export function get<T = any>(path: string, timeoutMs = 15000): Promise<T> {
  return request<T>(path, { method: 'GET' }, timeoutMs);
}

/** POST JSON 请求。 */
export function postJson<T = any>(path: string, body?: unknown, timeoutMs = 20000): Promise<T> {
  return request<T>(
    path,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body)
    },
    timeoutMs
  );
}

/** 触发浏览器下载一个后端返回的文件。 */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const url = apiUrl(path);

  let response: Response;
  try {
    response = await fetch(url);
  } catch (error) {
    throw new ApiError(describeError(error), url, null);
  }

  if (!response.ok) {
    throw new ApiError(describeStatus(response.status), url, response.status);
  }

  // 优先使用后端给出的文件名（Content-Disposition）。
  let fileName = fallbackName;
  const disposition = response.headers.get('content-disposition') || '';
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  const plainMatch = /filename="?([^";]+)"?/i.exec(disposition);
  if (utf8Match) {
    fileName = decodeURIComponent(utf8Match[1]);
  } else if (plainMatch) {
    fileName = plainMatch[1];
  }

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  try {
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    // 交给浏览器完成下载后再回收对象 URL。
    setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
  }
}

/** 格式化字节数为可读文本。 */
export function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / Math.pow(1024, index)).toFixed(index === 0 ? 0 : 2)} ${units[index]}`;
}
