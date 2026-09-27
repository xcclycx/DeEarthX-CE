import { pushLocalError } from '../stores/errorStore';

/**
 * 全局错误捕获。只在 <c>main.ts</c> 里安装一次。
 *
 * 覆盖三类前端异常：
 *  1. window 'error' —— 同步异常与资源加载失败
 *  2. window 'unhandledrejection' —— 未被 catch 的 Promise 拒绝
 *  3. console.error —— 兜底捕获第三方库只打日志、不抛出的错误
 *
 * 所有异常都会进入 errorStore，由 <c>ProcessErrorBell</c> 统一提示，
 * 而不是散落在各处各自弹窗。
 */

let installed = false;

/** 已在控制台打印过的错误不再重复捕获，避免递归。 */
let suppressConsoleCapture = false;

/** 压制期：应用启动阶段后端尚未就绪，避免刷屏。 */
let quietUntil = 0;

/** 忽略的模式：这些消息是已知的噪声，不属于程序错误。 */
const IGNORED_PATTERNS: RegExp[] = [
  /ResizeObserver loop/i,
  /Script error\.?$/i,
  /Download the Vue Devtools/i,
  /\[vite\]/i,
  /favicon\.ico/i,
  /net::ERR_CONNECTION_REFUSED.*37019/i,
  /WebSocket connection to .*37019/i
];

function shouldIgnore(message: string): boolean {
  return IGNORED_PATTERNS.some((pattern) => pattern.test(message));
}

function record(message: string, detail?: string, source = 'window') {
  if (!message || shouldIgnore(message)) {
    return;
  }

  // 启动静默期内不收集，但也不丢进控制台噪音。
  if (Date.now() < quietUntil) {
    return;
  }

  pushLocalError(message, { detail, source });
}

/**
 * 安装全局错误处理。
 *
 * @param quietMs 启动后的静默时长（毫秒），默认 3 秒。
 */
export function installGlobalErrorHandlers(quietMs = 3000): void {
  if (installed) {
    return;
  }
  installed = true;
  quietUntil = Date.now() + quietMs;

  window.addEventListener('error', (event: ErrorEvent) => {
    // 资源加载失败（img/script/link）没有 error 对象，只有 target。
    if (!event.error && event.target && event.target !== window) {
      const target = event.target as HTMLElement;
      const url = (target as HTMLImageElement).src || (target as HTMLLinkElement).href || '';
      if (url) {
        record(`资源加载失败: ${url}`, undefined, 'resource');
      }
      return;
    }

    record(
      event.message || '发生未知脚本错误',
      event.error instanceof Error ? event.error.stack : undefined,
      'window'
    );
  });

  window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
    const reason = event.reason;

    let message = '发生未处理的异步错误';
    let detail: string | undefined;

    if (reason instanceof Error) {
      message = reason.message || message;
      detail = reason.stack;
    } else if (typeof reason === 'string') {
      message = reason;
    } else if (reason && typeof reason === 'object') {
      // ApiError 之类的自定义错误对象。
      const maybe = reason as { message?: unknown; stack?: unknown };
      if (typeof maybe.message === 'string' && maybe.message) {
        message = maybe.message;
      }
      if (typeof maybe.stack === 'string') {
        detail = maybe.stack;
      }
    }

    record(message, detail, 'promise');
  });

  installConsoleCapture();
}

/**
 * 包装 console.error，把第三方库纯日志型的错误也纳入提醒。
 * 只记录第一条参数，且与真实 DOM 错误去重由 errorStore 负责。
 */
function installConsoleCapture(): void {
  const originalError = console.error.bind(console);

  console.error = (...args: unknown[]) => {
    if (!suppressConsoleCapture) {
      suppressConsoleCapture = true;
      try {
        const first = args[0];
        if (typeof first === 'string' && !first.startsWith('[')) {
          const detail = args
            .slice(1)
            .map((a) => (a instanceof Error ? a.stack || a.message : String(a)))
            .join(' ')
            .slice(0, 2000);
          record(first, detail || undefined, 'console');
        }
      } catch {
        // 捕获逻辑本身绝不能抛错。
      } finally {
        suppressConsoleCapture = false;
      }
    }

    originalError(...args);
  };
}
