/**
 * 插件系统前端 API 客户端。
 *
 * 之前每个视图都各自拼接 `import.meta.env.VITE_API_*` 并手写 fetch，
 * 导致同一份协议（`{ status, message, data }`）在 4 个文件里重复实现。
 * 这里集中一次，新增插件接口只需加一个方法。
 */

const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
const apiPort = import.meta.env.VITE_API_PORT || '37019';

/** 后端根地址，供需要 iframe / 直链的场景使用。 */
export const apiBaseUrl = `http://${apiHost}:${apiPort}`;

// ------------------------------------------------------------------ 类型

/** 插件能力分组。旧版的扁平字段仍然出现在响应里，这里只声明新结构。 */
export interface PluginCapabilities {
  sidebar?: boolean;
  pages?: boolean;
  injectScripts?: string[];
  injectStyles?: string[];
  hookEntry?: string;
  hooks?: string[];
  filterStrategies?: Array<{ name: string; type?: string; label?: string; hook?: string }>;
  permissions?: Record<string, string>;
  templates?: string[];
  scripts?: string[];
}

export interface PluginSidebarItem {
  key: string;
  label: string;
  icon?: string;
  route: string;
}

export interface PluginDependency {
  id: string;
  version?: string;
  optional?: boolean;
}

export interface PluginManifest {
  id: string;
  name: string;
  version: string;
  author?: string;
  description?: string;
  homepage?: string;
  /** 旧字段，与 homepage 同义。 */
  url?: string;
  license?: string;
  icon?: string;
  sourceUrl?: string;
  openSource?: boolean;
  minAppVersion?: string;
  manifestVersion?: number;
  capabilities?: PluginCapabilities;
  dependencies?: PluginDependency[];
  sidebarItems?: PluginSidebarItem[];
  defaultConfig?: Record<string, unknown>;
  configLabels?: Record<string, string>;
  configDescriptions?: Record<string, string>;
  autoStart?: boolean;
  lifecycle?: { start?: string; stop?: string };
  /** 旧字段，等于 capabilities.sidebar。 */
  hasSidebar?: boolean;
}

export interface PluginConfig {
  id: string;
  enabled: boolean;
  autoStart: boolean;
  settings: Record<string, unknown>;
}

export interface PluginInfo {
  manifest: PluginManifest;
  enabled: boolean;
  config: PluginConfig;
}

export interface PluginDetail extends PluginInfo {
  autoStart: boolean;
  state: string;
  error?: string | null;
  resolvedDependencies?: string[];
}

export interface PluginSettingField {
  key: string;
  type: string;
  label?: string;
  description?: string;
  required?: boolean;
  default?: unknown;
  options?: Array<{ value: string; label: string }>;
  min?: number;
  max?: number;
  order?: number;
}

export interface PluginConfigPayload {
  settings: Record<string, unknown>;
  defaults: Record<string, unknown>;
  /** 后端推断出的配置项描述，用于渲染正确的控件类型。 */
  schema?: PluginSettingField[];
}

export interface PluginInjections {
  pluginId: string;
  css: string[];
  js: string[];
}

export interface PluginDiagnosticsEntry {
  id: string;
  name?: string;
  version?: string;
  directory?: string;
  state: string;
  enabled: boolean;
  autoStart: boolean;
  error?: string | null;
  dependencies: string[];
  resolvedDependencies: string[];
  hooks: string[];
}

export interface PluginExtensionPoint {
  type: string;
  kind: string;
  count: number;
  owners: string[];
}

export interface PluginDiagnostics {
  total: number;
  enabled: number;
  loadedInMilliseconds: number;
  hotReload: boolean;
  plugins: PluginDiagnosticsEntry[];
  failures: PluginDiagnosticsEntry[];
  extensionPoints: PluginExtensionPoint[];
  declaredHooks: string[];
}

export interface PluginReloadReport {
  loaded: number;
  failed: number;
  elapsedMs: number;
  failures: Array<{ id: string; state: string; error: string }>;
}

export interface PluginValidationResult {
  isValid: boolean;
  errors: string[];
  warnings: string[];
}

/** 后端把业务错误放在 `message` 里，这里统一抛出，方便视图直接展示。 */
export class PluginApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'PluginApiError';
    this.status = status;
  }
}

// ------------------------------------------------------------------ 请求

interface ApiEnvelope<T> {
  status?: number;
  message?: string;
  data?: T;
  requirePassword?: boolean;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, init);

  let payload: ApiEnvelope<T> | null = null;
  try {
    payload = (await response.json()) as ApiEnvelope<T>;
  } catch {
    payload = null;
  }

  if (!response.ok || (payload?.status !== undefined && payload.status >= 400) || payload === null) {
    throw new PluginApiError(
      payload?.message || `请求失败（HTTP ${response.status}）`,
      payload?.status ?? response.status
    );
  }

  return payload.data as T;
}

const jsonInit = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body)
});

/** 触发浏览器下载（导出插件等）。 */
function download(path: string, fileName: string): void {
  const link = document.createElement('a');
  link.href = `${apiBaseUrl}${path}`;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
}

/** 加密导出需要携带密码，走 POST + Blob。 */
async function downloadPost(path: string, body: unknown, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${path}`, jsonInit(body));

  if (!response.ok) {
    let message = `导出失败（HTTP ${response.status}）`;
    try {
      const payload = (await response.json()) as ApiEnvelope<unknown>;
      message = payload?.message || message;
    } catch {
      // 非 JSON 响应，保留默认提示。
    }
    throw new PluginApiError(message, response.status);
  }

  const blob = await response.blob();
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(url);
}

// ------------------------------------------------------------------ 接口

export const pluginApi = {
  list: () => request<PluginInfo[]>('/plugins'),

  detail: (id: string) => request<PluginDetail>(`/plugins/${encodeURIComponent(id)}`),

  injections: () => request<PluginInjections[]>('/plugins/injections'),

  diagnostics: () => request<PluginDiagnostics>('/plugins/diagnostics'),

  /** 全量热重载：改完插件目录后无需重启应用。 */
  reloadAll: () => request<PluginReloadReport>('/plugins/reload', { method: 'POST' }),

  reload: (id: string) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/reload`, { method: 'POST' }),

  enable: (id: string) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/enable`, { method: 'POST' }),

  disable: (id: string) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/disable`, { method: 'POST' }),

  setAutoStart: (id: string, autoStart: boolean) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/autostart`, jsonInit({ autoStart })),

  getConfig: (id: string) => request<PluginConfigPayload>(`/plugins/${encodeURIComponent(id)}/config`),

  saveConfig: (id: string, settings: Record<string, unknown>) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/config`, jsonInit({ settings })),

  resetConfig: (id: string) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}/config/reset`, { method: 'POST' }),

  validate: (id: string) =>
    request<PluginValidationResult>(`/plugins/${encodeURIComponent(id)}/validate`),

  listFiles: (id: string) => request<string[]>(`/plugins/${encodeURIComponent(id)}/files`),

  create: (input: { name: string; author: string; url?: string }) =>
    request<{ id: string }>('/plugins/create', jsonInit(input)),

  uninstall: (id: string, keepConfig = true) =>
    request<unknown>(`/plugins/${encodeURIComponent(id)}?keepConfig=${keepConfig}`, { method: 'DELETE' }),

  /** 安装 .zip / .dxp。返回 requirePassword 时需要再调用 importEncrypted。 */
  async install(file: File): Promise<{ requirePassword: boolean; id?: string }> {
    const form = new FormData();
    form.append('file', file);

    const response = await fetch(`${apiBaseUrl}/plugins/install`, { method: 'POST', body: form });
    const payload = (await response.json()) as ApiEnvelope<{ id: string }>;

    if (payload?.requirePassword) {
      return { requirePassword: true };
    }

    if (!response.ok || payload?.status !== 200 || !payload.data) {
      throw new PluginApiError(payload?.message || '插件安装失败', payload?.status ?? response.status);
    }

    return { requirePassword: false, id: payload.data.id };
  },

  async importEncrypted(file: File, password: string): Promise<string> {
    const form = new FormData();
    form.append('file', file);
    form.append('password', password);

    const response = await fetch(`${apiBaseUrl}/plugins/install-encrypted`, { method: 'POST', body: form });
    const payload = (await response.json()) as ApiEnvelope<{ id: string }>;

    if (!response.ok || payload?.status !== 200 || !payload.data) {
      throw new PluginApiError(payload?.message || '解密失败，请检查密码', payload?.status ?? response.status);
    }

    return payload.data.id;
  },

  exportZip: (id: string) => download(`/plugins/${encodeURIComponent(id)}/export`, `${id}.zip`),

  exportEncrypted: (id: string, mode: 'public' | 'private', password: string, fileName: string) =>
    downloadPost(`/plugins/${encodeURIComponent(id)}/export-encrypted`, { mode, password }, fileName),

  openFolder: () => request<unknown>('/plugins/folder'),

  /** 插件页面地址，供 iframe 使用。 */
  pageUrl: (pluginId: string, pageKey: string) =>
    `${apiBaseUrl}/plugin-page/${encodeURIComponent(pluginId)}/${encodeURIComponent(pageKey)}`,

  /** 插件静态资源地址。 */
  fileUrl: (pluginId: string, relativePath: string) =>
    `${apiBaseUrl}/plugins/${encodeURIComponent(pluginId)}/files/${relativePath.replace(/^\/+/, '')}`
};

/** 生成 10 位数字插件 ID（仅用于创建表单的预览提示）。 */
export function generatePluginId(): string {
  let id = '';
  for (let i = 0; i < 10; i++) {
    id += Math.floor(Math.random() * 10).toString();
  }
  return id;
}

/** 把任意异常转成可展示的文案。 */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof PluginApiError) {
    return error.message || fallback;
  }
  if (error instanceof Error) {
    return error.message || fallback;
  }
  return fallback;
}
