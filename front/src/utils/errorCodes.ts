// 错误码定义
export enum ErrorCode {
  // 后端启动相关错误
  BACKEND_START_FAILED = 1001,
  BACKEND_PORT_OCCUPIED = 1002,
  BACKEND_CONNECTION_FAILED = 1003,
  BACKEND_RESPONSE_ERROR = 1004,
  
  // 网络相关错误
  NETWORK_ERROR = 2001,
  NETWORK_TIMEOUT = 2002,
  NETWORK_CONNECTION_REFUSED = 2003,
  
  // 文件相关错误
  FILE_NOT_FOUND = 3001,
  FILE_PERMISSION_ERROR = 3002,
  FILE_FORMAT_ERROR = 3003,
  FILE_SIZE_ERROR = 3004,
  
  // 系统相关错误
  JAVA_NOT_FOUND = 4001,
  DISK_SPACE_INSUFFICIENT = 4002,
  MEMORY_INSUFFICIENT = 4003,
  
  // 未知错误
  UNKNOWN_ERROR = 9999
}

// 错误信息映射
export const errorMessages: Record<ErrorCode, string> = {
  [ErrorCode.BACKEND_START_FAILED]: '后端服务启动失败',
  [ErrorCode.BACKEND_PORT_OCCUPIED]: '后端服务端口被占用',
  [ErrorCode.BACKEND_CONNECTION_FAILED]: '后端服务连接失败',
  [ErrorCode.BACKEND_RESPONSE_ERROR]: '后端服务响应错误',
  [ErrorCode.NETWORK_ERROR]: '网络连接错误',
  [ErrorCode.NETWORK_TIMEOUT]: '网络连接超时',
  [ErrorCode.NETWORK_CONNECTION_REFUSED]: '网络连接被拒绝',
  [ErrorCode.FILE_NOT_FOUND]: '文件未找到',
  [ErrorCode.FILE_PERMISSION_ERROR]: '文件权限错误',
  [ErrorCode.FILE_FORMAT_ERROR]: '文件格式错误',
  [ErrorCode.FILE_SIZE_ERROR]: '文件大小错误',
  [ErrorCode.JAVA_NOT_FOUND]: 'Java 未找到',
  [ErrorCode.DISK_SPACE_INSUFFICIENT]: '磁盘空间不足',
  [ErrorCode.MEMORY_INSUFFICIENT]: '内存不足',
  [ErrorCode.UNKNOWN_ERROR]: '未知错误'
};

// 错误建议映射
export const errorSuggestions: Record<ErrorCode, string[]> = {
  [ErrorCode.BACKEND_START_FAILED]: [
    '检查 37019 端口是否被占用',
    '检查后端服务是否正常',
    '重启应用程序'
  ],
  [ErrorCode.BACKEND_PORT_OCCUPIED]: [
    '关闭占用 37019 端口的其他应用',
    '检查是否有其他 DeEarthX 实例在运行',
    '重启计算机后再试'
  ],
  [ErrorCode.BACKEND_CONNECTION_FAILED]: [
    '检查后端服务是否正在运行',
    '检查网络连接是否正常',
    '重启应用程序'
  ],
  [ErrorCode.BACKEND_RESPONSE_ERROR]: [
    '检查后端服务是否正常',
    '重启后端服务',
    '联系技术支持'
  ],
  [ErrorCode.NETWORK_ERROR]: [
    '检查网络连接是否正常',
    '检查防火墙设置',
    '稍后重试'
  ],
  [ErrorCode.NETWORK_TIMEOUT]: [
    '检查网络连接速度',
    '稍后重试',
    '检查目标服务器是否可访问'
  ],
  [ErrorCode.NETWORK_CONNECTION_REFUSED]: [
    '检查目标服务器是否正在运行',
    '检查网络连接是否正常',
    '检查防火墙设置'
  ],
  [ErrorCode.FILE_NOT_FOUND]: [
    '确认文件路径是否正确',
    '检查文件是否存在',
    '重新上传文件'
  ],
  [ErrorCode.FILE_PERMISSION_ERROR]: [
    '检查文件权限设置',
    '以管理员身份运行应用程序',
    '检查文件是否被其他程序占用'
  ],
  [ErrorCode.FILE_FORMAT_ERROR]: [
    '确认文件格式是否正确',
    '重新上传正确格式的文件',
    '检查文件是否损坏'
  ],
  [ErrorCode.FILE_SIZE_ERROR]: [
    '检查文件大小是否符合要求',
    '压缩文件后再上传',
    '检查磁盘空间是否充足'
  ],
  [ErrorCode.JAVA_NOT_FOUND]: [
    '安装 Java 17 或更高版本',
    '配置 Java 环境变量',
    '重启应用程序'
  ],
  [ErrorCode.DISK_SPACE_INSUFFICIENT]: [
    '清理磁盘空间',
    '选择其他存储位置',
    '删除不必要的文件'
  ],
  [ErrorCode.MEMORY_INSUFFICIENT]: [
    '增加系统内存',
    '关闭其他占用内存的应用程序',
    '减少同时处理的任务数量'
  ],
  [ErrorCode.UNKNOWN_ERROR]: [
    '重启应用程序',
    '检查系统日志',
    '联系技术支持'
  ]
};

// 获取错误信息
export function getErrorMessage(code: ErrorCode): string {
  return errorMessages[code] || errorMessages[ErrorCode.UNKNOWN_ERROR];
}

// 获取错误建议
export function getErrorSuggestions(code: ErrorCode): string[] {
  return errorSuggestions[code] || errorSuggestions[ErrorCode.UNKNOWN_ERROR];
}

// 错误对象接口
export interface ErrorInfo {
  code: ErrorCode;
  message: string;
  details?: string;
  suggestions?: string[];
}

// 创建错误信息
export function createErrorInfo(code: ErrorCode, details?: string): ErrorInfo {
  return {
    code,
    message: getErrorMessage(code),
    details,
    suggestions: getErrorSuggestions(code)
  };
}
