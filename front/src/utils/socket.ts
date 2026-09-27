import { Socket } from 'socket.io-client';
import { getProgressSocket, getExistingSocket, disconnectProgressSocket } from '../stores/progressStore';

/**
 * Socket.IO 访问层。
 *
 * 这里刻意复用 progressStore 里的单例连接：整个应用只需要一条到后端的
 * Socket.IO 连接，之前这里会再建一条，两条连接的事件互不可见，既浪费
 * 资源又容易造成「状态不更新」的诡异问题。
 */

export interface SocketIOOptions {
  host?: string;
  port?: number;
  path?: string;
  transports?: string[];
  reconnection?: boolean;
  reconnectionAttempts?: number;
  reconnectionDelay?: number;
}

/**
 * 获取（必要时创建）共享的 Socket.IO 连接。
 *
 * @remarks 连接参数由 progressStore 统一决定，这里传入的 host/port 等仅作兼容保留。
 */
export function getSocketIO(_options: SocketIOOptions = {}): Socket {
  return getProgressSocket();
}

/** 断开并销毁共享连接。 */
export function disconnectSocket(): void {
  disconnectProgressSocket();
}

/** 读取当前连接（可能为 null）。 */
export function getSocket(): Socket | null {
  return getExistingSocket();
}

/** 发送事件（未连接时静默忽略并打印警告）。 */
export function emit(event: string, data?: any): void {
  const socket = getExistingSocket();
  if (socket && socket.connected) {
    socket.emit(event, data);
  } else {
    console.warn('Socket.IO 未连接，无法发送事件:', event);
  }
}

/** 注册事件。 */
export function on(event: string, callback: (...args: any[]) => void): void {
  getExistingSocket()?.on(event, callback);
}

/** 注销事件。 */
export function off(event: string, callback?: (...args: any[]) => void): void {
  const socket = getExistingSocket();
  if (!socket) return;
  if (callback) {
    socket.off(event, callback);
  } else {
    socket.removeAllListeners(event);
  }
}

/** 连接是否可用。 */
export function isConnected(): boolean {
  const socket = getExistingSocket();
  return socket !== null && socket.connected;
}
