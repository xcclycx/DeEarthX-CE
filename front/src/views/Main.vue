<script lang="ts" setup>
import { inject, ref, onMounted, computed, onUnmounted, watch } from 'vue';
import { InboxOutlined } from '@ant-design/icons-vue';
import { message, notification, StepsProps } from 'ant-design-vue';
import type { UploadFile, UploadChangeParam } from 'ant-design-vue';
import { sendNotification } from '@tauri-apps/plugin-notification';
import { SelectProps } from 'ant-design-vue/es/vc-select';
import { useI18n } from 'vue-i18n';
import { getProgressState, getProgressSocket, getExistingSocket, resetProgressState } from '../stores/progressStore';
import type { Socket } from 'socket.io-client';

const { t } = useI18n();

interface Template {
    id: string;
    metadata: {
        name: string;
        version: string;
        description: string;
        author: string;
        created: string;
        type: string;
    };
}

// 进度步骤配置 - 使用共享 store 状态
const progressState = getProgressState();

// 模板选择相关
const showTemplateModal = ref(false);
const templates = ref<Template[]>([]);
const loadingTemplates = ref(false);
const selectedTemplate = ref<string>('0');

// 步骤项（使用computed自动响应语言变化）
const stepItems = computed<Required<StepsProps>['items']>(() => {
    return [
        { title: t('home.step1_title'), description: t('home.step1_desc') },
        { title: t('home.step2_title'), description: t('home.step2_desc') },
        { title: t('home.step3_title'), description: t('home.step3_desc') },
        { title: t('home.step4_title'), description: t('home.step4_desc') }
    ];
});

// 文件上传相关
const uploadedFiles = ref<UploadFile[]>([]);
const uploadDisabled = ref(false);
const startButtonDisabled = ref(false);
const selectedFileName = ref<string>('');

// 阻止默认上传行为
function beforeUpload() {
    return false;
}

// 处理文件上传变更
function handleFileChange(info: UploadChangeParam) {
    if (info.file.status === 'removed') {
        uploadDisabled.value = false;
        selectedFileName.value = '';
        return;
    }

    if (info.file.status === 'uploading') {
        message.loading(t('home.preparing_file'));
        return;
    }

    if (info.file.status === 'done') {
        message.success(t('home.file_prepared'));
    }

    if (!info.file.name?.endsWith('.zip') && !info.file.name?.endsWith('.mrpack')) {
        message.error(t('home.only_zip_mrpack'));
        return;
    }
    uploadDisabled.value = true;
    selectedFileName.value = info.file.name;
}

// 处理文件拖拽（预留功能）
function handleFileDrop(e: DragEvent) {
    console.log(e);
}

// 初始化
const introPanelIndex = ref(0); // 0=软件介绍, 1=软件TIP
const showComplete = ref(false);
let introRotationTimer: ReturnType<typeof setInterval> | null = null;

function startIntroRotation() {
    stopIntroRotation();
    introRotationTimer = setInterval(() => {
        introPanelIndex.value = introPanelIndex.value === 0 ? 1 : 0;
    }, 5000);
}

function stopIntroRotation() {
    if (introRotationTimer !== null) {
        clearInterval(introRotationTimer);
        introRotationTimer = null;
    }
}

onMounted(() => {
    if (progressState.isMaking) {
        startIntroRotation();
    }
});

watch(() => progressState.isMaking, (val) => {
    if (val) {
        startIntroRotation();
    } else {
        stopIntroRotation();
        introPanelIndex.value = 0;
    }
});

onUnmounted(() => {
    stopIntroRotation();
});

// 重置所有状态
function resetState() {
    uploadedFiles.value = [];
    uploadDisabled.value = false;
    startButtonDisabled.value = false;
    selectedFileName.value = '';
    showComplete.value = false;
    resetProgressState();
    // 移除可能在等待连接的回调，避免重置后又自动开始上传。
    if (socket && startOnConnect) {
        socket.off('connect', startOnConnect);
    }
    startOnConnect = null;
    const killCoreProcess = inject("killCoreProcess");
    if (killCoreProcess && typeof killCoreProcess === 'function') {
        killCoreProcess();
    }
}

// 模式选择相关
const javaAvailable = ref(true);
const selectedMode = ref(javaAvailable.value ? 'server' : 'upload');

// 模式选项（使用computed自动响应语言变化）
const modeOptions = computed<SelectProps['options']>(() => {
    return [
        { label: t('home.mode_server'), value: 'server', disabled: !javaAvailable.value },
        { label: t('home.mode_upload'), value: 'upload', disabled: false }
    ];
});

// 处理模式选择
function handleModeSelect(value: string) {
    selectedMode.value = value;
}

// 加载模板列表
async function loadTemplates() {
    loadingTemplates.value = true;
    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        const response = await fetch(`http://${apiHost}:${apiPort}/templates`);
        const result = await response.json();
        
        if (result.status === 200) {
            templates.value = result.data || [];
        } else {
            message.error(t('home.template_load_failed'));
        }
    } catch (error) {
        console.error('加载模板列表失败:', error);
        message.error(t('home.template_load_failed'));
    } finally {
        loadingTemplates.value = false;
    }
}

// 打开模板选择弹窗
function openTemplateModal() {
    loadTemplates();
    showTemplateModal.value = true;
}

// 选择模板
function selectTemplate(templateId: string) {
    selectedTemplate.value = templateId;
    showTemplateModal.value = false;
    if (templateId === '0') {
        message.success(t('home.template_selected') + ': ' + t('home.template_official_loader'));
    } else {
        const template = templates.value.find(t => t.id === templateId);
        if (template) {
            message.success(t('home.template_selected') + ': ' + template.metadata.name);
        }
    }
}







// 获取当前选择的模板名称
const currentTemplateName = computed(() => {
    if (selectedTemplate.value === '0' || !selectedTemplate.value) {
        return t('home.template_official_loader');
    }
    const template = templates.value.find(t => t.id === selectedTemplate.value);
    return template ? template.metadata.name : t('home.template_official_loader');
});

// 进度显示相关 - 使用共享 store
const unzipProgress = progressState.unzipProgress;
const downloadProgress = progressState.downloadProgress;
const uploadProgress = progressState.uploadProgress;
const serverInstallProgress = progressState.serverInstallProgress;
const filterModsProgress = progressState.filterModsProgress;
const serverInstallInfo = progressState.serverInstallInfo;
const filterModsInfo = progressState.filterModsInfo;
let lastDownloadUpdateTime = 0;
let _downloadPendingIndex = 0;
let _downloadPendingTotal = 0;

// 格式化文件大小
function formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
}

// 格式化时间
function formatTime(seconds: number): string {
    if (seconds < 60) return `${Math.round(seconds)}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
    return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
}

// 运行DeEarthX核心功能
async function runDeEarthX(file: File) {
    message.success(t('home.start_production'));
    progressState.isMaking = true;
    progressState.showSteps = true;

    const formData = new FormData();
    formData.append('file', file);

    try {
        message.loading(t('home.task_preparing'));
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        let url = `http://${apiHost}:${apiPort}/start?mode=${selectedMode.value}`;
        
        if (selectedMode.value === 'server' && selectedTemplate.value) {
            url += `&template=${encodeURIComponent(selectedTemplate.value)}`;
        }

        uploadProgress.status = 'active';
        uploadProgress.percent = 0;
        uploadProgress.display = true;
        progressState.startTime = Date.now();

        await new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open('POST', url, true);

            xhr.upload.addEventListener('progress', (event) => {
                if (event.lengthComputable) {
                    const percent = Math.round((event.loaded / event.total) * 100);
                    uploadProgress.percent = percent;
                    uploadProgress.uploadedSize = event.loaded;
                    uploadProgress.totalSize = event.total;
                    
                    const elapsedTime = (Date.now() - progressState.startTime) / 1000;
                    if (elapsedTime > 0) {
                        uploadProgress.speed = event.loaded / elapsedTime;
                        const remainingBytes = event.total - event.loaded;
                        uploadProgress.remainingTime = remainingBytes / uploadProgress.speed;
                    }
                }
            });

            xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    uploadProgress.status = 'success';
                    uploadProgress.percent = 100;
                    setTimeout(() => {
                        uploadProgress.display = false;
                    }, 2000);
                    resolve(xhr.response);
                } else {
                    uploadProgress.status = 'exception';
                    reject(new Error(`HTTP ${xhr.status}`));
                }
            });

            xhr.addEventListener('error', () => {
                uploadProgress.status = 'exception';
                reject(new Error('网络错误'));
            });

            xhr.addEventListener('abort', () => {
                uploadProgress.status = 'exception';
                reject(new Error('上传已取消'));
            });

            xhr.send(formData);
        });
    } catch (error) {
        console.error('请求失败:', error);
        message.error(t('home.request_failed'));
        uploadProgress.status = 'exception';
        resetState();
    }
}

// 设置WebSocket连接
// 处理错误消息
function handleError(result: any) {
    if (result === 'jini') {
        javaAvailable.value = false;
        notification.error({
            message: t('home.java_error_title'),
            description: t('home.java_error_desc'),
            duration: 0
        });
    } else if (typeof result === 'string') {
        // 根据错误类型提供不同的解决方案
        let errorTitle = t('home.backend_error');
        let errorDesc = t('home.backend_error_desc', { error: result });
        let suggestions: string[] = [];

        // 网络相关错误
        if (result.includes('network') || result.includes('connection') || result.includes('timeout')) {
            errorTitle = t('home.network_error_title');
            errorDesc = t('home.network_error_desc', { error: result });
            suggestions = [
                t('home.suggestion_check_network'),
                t('home.suggestion_check_firewall'),
                t('home.suggestion_retry')
            ];
        }
        // 文件相关错误
        else if (result.includes('file') || result.includes('permission') || result.includes('disk')) {
            errorTitle = t('home.file_error_title');
            errorDesc = t('home.file_error_desc', { error: result });
            suggestions = [
                t('home.suggestion_check_disk_space'),
                t('home.suggestion_check_permission'),
                t('home.suggestion_check_file_format')
            ];
        }
        // 内存相关错误
        else if (result.includes('memory') || result.includes('out of memory') || result.includes('heap')) {
            errorTitle = t('home.memory_error_title');
            errorDesc = t('home.memory_error_desc', { error: result });
            suggestions = [
                t('home.suggestion_increase_memory'),
                t('home.suggestion_close_other_apps'),
                t('home.suggestion_restart_application')
            ];
        }
        // 通用错误
        else {
            suggestions = [
                t('home.suggestion_check_backend'),
                t('home.suggestion_check_logs'),
                t('home.suggestion_contact_support')
            ];
        }

        // 构建完整的错误描述
        const fullDescription = `${errorDesc}\n\n${t('home.suggestions')}:\n${suggestions.map((s, i) => `${i + 1}. ${s}`).join('\n')}`;

        notification.error({
            message: errorTitle,
            description: fullDescription,
            duration: 0
        });

        resetState();
    } else {
        notification.error({
            message: t('home.unknown_error_title'),
            description: t('home.unknown_error_desc'),
            duration: 0
        });
        resetState();
    }
}

// 更新解压进度
function updateUnzipProgress(result: { current: number; total: number }) {
    unzipProgress.percent = Math.round((result.current / result.total) * 100);
    if (result.current === result.total) {
        unzipProgress.status = 'success';
        setTimeout(() => {
            unzipProgress.display = false;
        }, 2000);
    }
}

// 更新下载进度
function updateDownloadProgress(result: { index: number; total: number; name?: string }) {
    _downloadPendingIndex = result.index;
    _downloadPendingTotal = result.total;

    const now = Date.now();
    if (now - lastDownloadUpdateTime < 3000 && _downloadPendingIndex < _downloadPendingTotal) return;
    lastDownloadUpdateTime = now;

    _flushDownloadProgress();
}

function _flushDownloadProgress() {
    progressState.downloadCompleted = Math.max(progressState.downloadCompleted, _downloadPendingIndex);
    progressState.downloadTotal = _downloadPendingTotal;
    downloadProgress.percent = Math.round((progressState.downloadCompleted / _downloadPendingTotal) * 100);
    if (downloadProgress.percent >= 100) {
        downloadProgress.status = 'success';
        setTimeout(() => {
            downloadProgress.display = false;
        }, 2000);
    }
}

// 处理完成状态
function handleFinish(result: number) {
    const timeSpent = Math.round(result / 1000);
    progressState.currentStep++;
    showComplete.value = true;
    message.success(t('home.production_complete', { time: timeSpent }));
    sendNotification({ title: t('common.app_name'), body: t('home.production_complete', { time: timeSpent }) });

    setTimeout(resetState, 5000);
}

// 处理服务端安装开始
function handleServerInstallStart(result: any) {
    serverInstallInfo.modpackName = result.modpackName;
    serverInstallInfo.minecraftVersion = result.minecraftVersion;
    serverInstallInfo.loaderType = result.loaderType;
    serverInstallInfo.loaderVersion = result.loaderVersion;
    serverInstallInfo.currentStep = '';
    serverInstallInfo.stepIndex = 0;
    serverInstallInfo.totalSteps = 0;
    serverInstallInfo.message = 'Starting installation...';
    serverInstallInfo.status = 'installing';
    serverInstallInfo.error = '';
    serverInstallInfo.installPath = '';
    serverInstallInfo.duration = 0;
    serverInstallProgress.status = 'active';
    serverInstallProgress.percent = 0;
    serverInstallProgress.display = true;
}

// 处理服务端安装步骤
function handleServerInstallStep(result: any) {
    serverInstallInfo.currentStep = result.step;
    serverInstallInfo.stepIndex = result.stepIndex;
    serverInstallInfo.totalSteps = result.totalSteps;
    serverInstallInfo.message = result.message || result.step;
    
    const overallProgress = (result.stepIndex / result.totalSteps) * 100;
    serverInstallProgress.percent = Math.round(overallProgress);
}

// 处理服务端安装进度
function handleServerInstallProgress(result: any) {
    serverInstallInfo.currentStep = result.step;
    serverInstallInfo.message = result.message || result.step;
    serverInstallProgress.percent = result.progress;
}

// 处理服务端安装完成
function handleServerInstallComplete(result: any) {
    serverInstallInfo.status = 'completed';
    serverInstallInfo.installPath = result.installPath;
    serverInstallInfo.duration = result.duration;
    serverInstallInfo.message = t('home.server_install_completed');
    serverInstallProgress.status = 'success';
    serverInstallProgress.percent = 100;
    serverInstallProgress.display = true;
    
    progressState.currentStep++;
    showComplete.value = true;
    
    const timeSpent = Math.round(result.duration / 1000);
    message.success(t('home.server_install_completed') + ` ${t('home.server_install_duration')}: ${timeSpent}s`);
    sendNotification({ title: t('common.app_name'), body: t('home.production_complete', { time: timeSpent }) });
    
    setTimeout(() => {
        serverInstallProgress.display = false;
    }, 8000);
    
    setTimeout(() => {
        progressState.isMaking = false;
        showComplete.value = false;
        uploadedFiles.value = [];
        uploadDisabled.value = false;
        startButtonDisabled.value = false;
        selectedFileName.value = '';
        resetProgressState();
    }, 15000);
}

// 处理服务端安装错误
function handleServerInstallError(result: any) {
    serverInstallInfo.status = 'error';
    serverInstallInfo.error = result.error;
    serverInstallInfo.message = result.error;
    serverInstallProgress.status = 'exception';
    
    notification.error({
        message: t('home.server_install_error'),
        description: result.error,
        duration: 0
    });
}

// 处理筛选模组开始
function handleFilterModsStart(result: any) {
    filterModsInfo.totalMods = result.totalMods;
    filterModsInfo.currentMod = 0;
    filterModsInfo.modName = '';
    filterModsInfo.filteredCount = 0;
    filterModsInfo.movedCount = 0;
    filterModsInfo.status = 'filtering';
    filterModsInfo.error = '';
    filterModsInfo.duration = 0;
    filterModsProgress.status = 'active';
    filterModsProgress.percent = 0;
    filterModsProgress.display = true;
}

// 处理筛选模组进度
function handleFilterModsProgress(result: any) {
    filterModsInfo.currentMod = result.current;
    filterModsInfo.modName = result.modName;
    
    const percent = Math.round((result.current / result.total) * 100);
    filterModsProgress.percent = percent;
}

// 处理筛选模组完成
function handleFilterModsComplete(result: any) {
    filterModsInfo.status = 'completed';
    filterModsInfo.filteredCount = result.filteredCount;
    filterModsInfo.movedCount = result.movedCount;
    filterModsInfo.duration = result.duration;
    filterModsProgress.status = 'success';
    filterModsProgress.percent = 100;
    filterModsProgress.display = true;
    
    const timeSpent = Math.round(result.duration / 1000);
    message.success(t('home.filter_mods_completed', { filtered: result.filteredCount, moved: result.movedCount }) + ` ${t('home.server_install_duration')}: ${timeSpent}s`);
    
    setTimeout(() => {
        filterModsProgress.display = false;
    }, 8000);
}

// 处理筛选模组错误
function handleFilterModsError(result: any) {
    filterModsInfo.status = 'error';
    filterModsInfo.error = result.error;
    filterModsProgress.status = 'exception';
    
    notification.error({
        message: t('home.filter_mods_error'),
        description: result.error,
        duration: 0
    });
}

// Socket.IO 引用 - 使用 store 管理的 socket
let socket: Socket | null = null;

// ------------------------------------------------------------------
// 导出优化报告
// ------------------------------------------------------------------

/**
 * 后端优化流水线推送的报告。做成可查看的弹窗，
 * 让用户知道到底移除了什么、生成了什么，而不是黑盒处理。
 */
const optimizationReport = ref<any>(null);
const optimizationReportVisible = ref(false);

function handleOptimizationReport(result: any) {
    if (!result) return;

    optimizationReport.value = result;

    const removed = (result.removedClientResources?.length ?? 0);
    const sensitive = (result.sanitizedSensitiveFiles?.length ?? 0);
    const scripts = (result.generatedScripts?.length ?? 0);

    message.success(
        `${t('setting.optimize_enabled_name')}: ${result.memoryMb}MB · ${removed + sensitive} ${t('setting.optimize_report_removed')} · ${scripts} ${t('setting.optimize_report_generated_scripts')}`,
        8
    );
}

function openOptimizationReport() {
    if (!optimizationReport.value) {
        message.info(t('setting.optimize_report_none'));
        return;
    }
    optimizationReportVisible.value = true;
}

/** 把报告里的数组安全地转成展示用字符串数组。 */
function reportList(value: unknown): string[] {
    return Array.isArray(value) ? value.map((v) => String(v)) : [];
}

// 已注册的事件处理函数引用，用于清理
const socketHandlers: Record<string, (...args: any[]) => void> = {};

// 注册 Socket 事件监听器
function registerSocketListeners(sock: Socket) {
    // 先清理旧的监听器
    unregisterSocketListeners(sock);

    socketHandlers['connect'] = () => {
        message.success(t('home.ws_connected'));
    };
    socketHandlers['error'] = (data: any) => {
        try {
            const parsed = typeof data === 'string' ? JSON.parse(data) : data;
            handleError(parsed.message);
        } catch {
            handleError(data);
        }
    };
    socketHandlers['info'] = (data: any) => {
        try {
            const parsed = typeof data === 'string' ? JSON.parse(data) : data;
            if (parsed.message) {
                message.info(parsed.message);
            }
        } catch {
            message.info(data);
        }
    };
    socketHandlers['changed'] = () => {
        progressState.currentStep++;
    };
    socketHandlers['unzip'] = (data: any) => {
        updateUnzipProgress(data);
    };
    socketHandlers['downloading'] = (data: any) => {
        updateDownloadProgress(data);
    };
    socketHandlers['finish'] = (data: any) => {
        handleFinish(data);
    };
    socketHandlers['server_install_start'] = (data: any) => {
        handleServerInstallStart(data);
    };
    socketHandlers['server_install_step'] = (data: any) => {
        handleServerInstallStep(data);
    };
    socketHandlers['server_install_progress'] = (data: any) => {
        handleServerInstallProgress(data);
    };
    socketHandlers['server_install_complete'] = (data: any) => {
        handleServerInstallComplete(data);
    };
    socketHandlers['server_install_error'] = (data: any) => {
        handleServerInstallError(data);
    };
    socketHandlers['filter_mods_start'] = (data: any) => {
        handleFilterModsStart(data);
    };
    socketHandlers['filter_mods_progress'] = (data: any) => {
        handleFilterModsProgress(data);
    };
    socketHandlers['filter_mods_complete'] = (data: any) => {
        handleFilterModsComplete(data);
    };
    socketHandlers['filter_mods_error'] = (data: any) => {
        handleFilterModsError(data);
    };
    socketHandlers['optimization_report'] = (data: any) => {
        handleOptimizationReport(data);
    };
    socketHandlers['connect_error'] = () => {
        notification.error({
            message: t('home.ws_error_title'),
            description: `${t('home.ws_error_desc')}\n\n${t('home.suggestions')}:\n1. ${t('home.suggestion_check_backend')}\n2. ${t('home.suggestion_check_port')}\n3. ${t('home.suggestion_restart_application')}`,
            duration: 0
        });
        resetState();
    };

    // 注册所有处理器
    for (const [event, handler] of Object.entries(socketHandlers)) {
        sock.on(event, handler);
    }
}

// 移除 Socket 事件监听器
function unregisterSocketListeners(sock: Socket | null) {
    if (!sock) return;
    for (const [event, handler] of Object.entries(socketHandlers)) {
        sock.off(event, handler);
    }
    // 清空处理器引用
    for (const key of Object.keys(socketHandlers)) {
        delete socketHandlers[key];
    }
}

// 开始处理文件
function handleStartProcess() {
    if (uploadedFiles.value.length === 0) {
        message.warning(t('home.please_select_file'));
        return;
    }

    const file = uploadedFiles.value[0].originFileObj;
    if (!file) return;

    startButtonDisabled.value = true;
    uploadDisabled.value = true;
    progressState.showSteps = true;

    message.loading(t('home.ws_connecting'));
    
    socket = getProgressSocket();
    registerSocketListeners(socket);

    // 首次连接时才开始上传。这里用 once 语义的处理器，
    // 避免重复点击开始按钮时把同一个回调叠加多次。
    if (socket.connected) {
        message.success(t('home.ws_connected'));
        runDeEarthX(file);
    } else {
        socket.off('connect', socketHandlers['connect']);
        if (startOnConnect) {
            socket.off('connect', startOnConnect);
        }
        startOnConnect = () => {
            message.success(t('home.ws_connected'));
            runDeEarthX(file);
        };
        socket.once('connect', startOnConnect);
    }
}

/** 待连接后触发的开始回调，用于在取消/重置时移除。 */
let startOnConnect: (() => void) | null = null;

// 组件挂载时恢复进度状态
onMounted(() => {
    if (progressState.isMaking) {
        // 如果正在制作中，恢复显示并重新注册 socket 监听器
        const existingSocket = getExistingSocket();
        if (existingSocket && existingSocket.connected) {
            socket = existingSocket;
            registerSocketListeners(existingSocket);
        }
    }
});

// 组件卸载时移除事件监听器，但保持 socket 连接
onUnmounted(() => {
    unregisterSocketListeners(socket);
    socket = null;
    // 不主动断开 socket，由 store 管理生命周期
});
</script>
<template>
    <div class="tw:h-full tw:w-full tw:relative tw:flex tw:flex-col">
        <div class="tw:flex-1 tw:w-full tw:flex tw:flex-col tw:justify-center tw:items-center tw:p-4">
            <!-- 制作中：显示软件介绍和TIP（循环轮播） -->
            <div v-if="progressState.isMaking" class="tw:w-full tw:max-w-2xl tw:flex tw:flex-col tw:items-center tw:pb-24 tw:pr-72">
                <!-- 完成提示 -->
                <div v-if="showComplete" class="tw:w-full tw:bg-white tw:rounded-xl tw:shadow-md tw:p-8 tw:text-center tw:animate-pulse">
                    <div class="tw:text-4xl tw:mb-3">🎉</div>
                    <h2 class="tw:text-2xl tw:font-bold tw:text-green-600 tw:mb-2">{{ t('home.complete_title') }}</h2>
                    <p class="tw:text-sm tw:text-gray-500">{{ t('home.complete_auto_reset') }}</p>
                    <a-button
                        v-if="optimizationReport"
                        type="link"
                        class="tw:mt-2"
                        @click.stop="openOptimizationReport"
                    >
                        {{ t('setting.optimize_preview_button') }}
                    </a-button>
                </div>
                <template v-else>
                <transition name="fade" mode="out-in">
                    <div v-if="introPanelIndex === 0" key="intro" class="tw:w-full tw:bg-white tw:rounded-xl tw:shadow-md tw:p-6">
                        <h2 class="tw:text-lg tw:font-bold tw:text-gray-800 tw:mb-3 tw:flex tw:items-center tw:gap-2">
                            <span class="tw:text-blue-500">📖</span> {{ t('home.software_intro_title') }}
                        </h2>
                        <p class="tw:text-sm tw:text-gray-600 tw:mb-4 tw:leading-relaxed">{{ t('home.software_intro_desc') }}</p>
                        <div class="tw:grid tw:grid-cols-2 tw:gap-3">
                            <div class="tw:flex tw:items-start tw:gap-2 tw:p-2 tw:bg-blue-50 tw:rounded-lg">
                                <span class="tw:text-blue-500 tw:text-sm tw:mt-0.5 tw:shrink-0">🔍</span>
                                <span class="tw:text-xs tw:text-gray-700 tw:leading-relaxed">{{ t('home.software_intro_feature1') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:p-2 tw:bg-green-50 tw:rounded-lg">
                                <span class="tw:text-green-500 tw:text-sm tw:mt-0.5 tw:shrink-0">⚙️</span>
                                <span class="tw:text-xs tw:text-gray-700 tw:leading-relaxed">{{ t('home.software_intro_feature2') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:p-2 tw:bg-purple-50 tw:rounded-lg">
                                <span class="tw:text-purple-500 tw:text-sm tw:mt-0.5 tw:shrink-0">📦</span>
                                <span class="tw:text-xs tw:text-gray-700 tw:leading-relaxed">{{ t('home.software_intro_feature3') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:p-2 tw:bg-orange-50 tw:rounded-lg">
                                <span class="tw:text-orange-500 tw:text-sm tw:mt-0.5 tw:shrink-0">⚡</span>
                                <span class="tw:text-xs tw:text-gray-700 tw:leading-relaxed">{{ t('home.software_intro_feature4') }}</span>
                            </div>
                        </div>
                    </div>
                    <div v-else key="tips" class="tw:w-full tw:bg-white tw:rounded-xl tw:shadow-md tw:p-6">
                        <h2 class="tw:text-lg tw:font-bold tw:text-gray-800 tw:mb-3 tw:flex tw:items-center tw:gap-2">
                            <span class="tw:text-yellow-500">💡</span> {{ t('home.software_tip_title') }}
                        </h2>
                        <div class="tw:flex tw:flex-col tw:gap-2">
                            <div class="tw:flex tw:items-start tw:gap-2 tw:text-xs tw:text-gray-600 tw:leading-relaxed">
                                <span class="tw:text-yellow-500 tw:shrink-0 tw:mt-0.5">1.</span>
                                <span>{{ t('home.software_tip1') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:text-xs tw:text-gray-600 tw:leading-relaxed">
                                <span class="tw:text-yellow-500 tw:shrink-0 tw:mt-0.5">2.</span>
                                <span>{{ t('home.software_tip2') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:text-xs tw:text-gray-600 tw:leading-relaxed">
                                <span class="tw:text-yellow-500 tw:shrink-0 tw:mt-0.5">3.</span>
                                <span>{{ t('home.software_tip3') }}</span>
                            </div>
                            <div class="tw:flex tw:items-start tw:gap-2 tw:text-xs tw:text-gray-600 tw:leading-relaxed">
                                <span class="tw:text-yellow-500 tw:shrink-0 tw:mt-0.5">4.</span>
                                <span>{{ t('home.software_tip4') }}</span>
                            </div>
                        </div>
                    </div>
                </transition>
                <!-- 轮播指示器 -->
                <div class="tw:flex tw:gap-2 tw:mt-4">
                    <span :class="['tw:w-2 tw:h-2 tw:rounded-full tw:transition-colors', introPanelIndex === 0 ? 'tw:bg-blue-500' : 'tw:bg-gray-300']"></span>
                    <span :class="['tw:w-2 tw:h-2 tw:rounded-full tw:transition-colors', introPanelIndex === 1 ? 'tw:bg-yellow-500' : 'tw:bg-gray-300']"></span>
                </div>
                </template>
            </div>
            <!-- 未制作：显示上传区域 -->
            <div v-else class="tw:w-full tw:max-w-2xl tw:flex tw:flex-col tw:items-center">
                <div>
                    <h1 class="tw:text-4xl tw:text-center tw:animate-pulse">{{ t('common.app_name') }}</h1>
                    <h1 class="tw:text-sm tw:text-gray-500 tw:text-center">{{ t('home.title') }}</h1>
                </div>
                <a-upload-dragger :disabled="uploadDisabled" class="tw:w-full tw:max-w-md tw:h-48" name="file"
                    action="/" :multiple="false" :before-upload="beforeUpload" @change="handleFileChange"
                    @drop="handleFileDrop" v-model:fileList="uploadedFiles" :show-upload-list="false" accept=".zip,.mrpack">
                    <p class="ant-upload-drag-icon">
                        <inbox-outlined></inbox-outlined>
                    </p>
                    <p class="ant-upload-text">{{ t('home.upload_title') }}</p>
                    <p class="ant-upload-hint">
                        {{ t('home.upload_hint') }}
                    </p>
                </a-upload-dragger>
                <div v-if="selectedFileName" class="tw:mt-3 tw:px-4 tw:py-2 tw:bg-green-50 tw:border tw:border-green-300 tw:rounded-lg tw:text-green-700 tw:text-sm tw:font-medium tw:text-center">
                    已选择：{{ selectedFileName }}
                </div>
                <div class="tw:flex tw:items-center tw:gap-2 tw:mt-8">
                    <a-select ref="select" :options="modeOptions" :value="selectedMode"
                        style="width: 120px;" @select="handleModeSelect"></a-select>
                    <a-button v-if="selectedMode === 'server'" @click="openTemplateModal">
                        {{ t('home.template_select_button') }}
                    </a-button>
                </div>
                <div v-if="selectedMode === 'server'" class="tw:text-xs tw:text-gray-500 tw:mt-2">
                    {{ t('home.template_selected') }}: {{ currentTemplateName }}
                </div>
                <a-button :disabled="startButtonDisabled" type="primary" @click="handleStartProcess"
                    style="margin-top: 6px">
                    {{ t('common.start') }}
                </a-button>
            </div>
        </div>
        <div v-if="progressState.showSteps"
            class="tw:fixed tw:bottom-2 tw:left-1/2 tw:-translate-x-1/2 tw:w-[65%] tw:h-20 tw:flex tw:justify-center tw:items-center tw:text-sm tw:bg-white tw:rounded-xl tw:shadow-lg tw:px-4 tw:ml-10">
            <a-steps :current="progressState.currentStep" :items="stepItems" size="small" />
        </div>
        <div v-if="progressState.showSteps" ref="logContainer"
            class="tw:absolute tw:right-2 tw:bottom-32 tw:h-80 tw:w-64 tw:rounded-xl tw:overflow-y-auto">
            <a-card :title="t('home.progress_title')" :bordered="true" class="tw:h-full">
                <div v-if="uploadProgress.display" class="tw:mb-4">
                    <h1 class="tw:text-sm">{{ t('home.upload_progress') }}</h1>
                    <a-progress :percent="uploadProgress.percent" :status="uploadProgress.status" size="small" />
                    <div v-if="uploadProgress.totalSize" class="tw:text-xs tw:text-gray-500 tw:mt-1">
                        {{ formatFileSize(uploadProgress.uploadedSize || 0) }} / {{ formatFileSize(uploadProgress.totalSize) }}
                        <span v-if="uploadProgress.speed" class="tw:ml-2">
                            {{ t('home.speed') }}: {{ formatFileSize(uploadProgress.speed) }}/s
                        </span>
                        <span v-if="uploadProgress.remainingTime" class="tw:ml-2">
                            {{ t('home.remaining') }}: {{ formatTime(uploadProgress.remainingTime) }}
                        </span>
                    </div>
                </div>
                <div v-if="unzipProgress.display" class="tw:mb-4">
                    <h1 class="tw:text-sm">{{ t('home.unzip_progress') }}</h1>
                    <a-progress :percent="unzipProgress.percent" :status="unzipProgress.status" size="small" />
                </div>
                <div v-if="downloadProgress.display" class="tw:mb-4">
                    <h1 class="tw:text-sm">{{ t('home.download_progress') }}</h1>
                    <a-progress :percent="downloadProgress.percent" :status="downloadProgress.status" size="small" />
                    <div class="tw:text-xs tw:text-gray-400 tw:mt-1">
                        下载 {{ progressState.downloadCompleted }}/{{ progressState.downloadTotal }}
                    </div>
                </div>
                <div v-if="serverInstallProgress.display" class="tw:mb-4">
                    <h1 class="tw:text-sm">{{ t('home.server_install_progress') }}</h1>
                    <a-progress :percent="serverInstallProgress.percent" :status="serverInstallProgress.status" size="small" />
                    <div v-if="serverInstallInfo.currentStep" class="tw:text-xs tw:text-gray-500 tw:mt-1">
                        {{ t('home.server_install_step') }}: {{ serverInstallInfo.currentStep }}
                        <span v-if="serverInstallInfo.totalSteps > 0">
                            ({{ serverInstallInfo.stepIndex }}/{{ serverInstallInfo.totalSteps }})
                        </span>
                    </div>
                    <div v-if="serverInstallInfo.message" class="tw:text-xs tw:text-gray-600 tw:mt-1 tw:break-words">
                        {{ t('home.server_install_message') }}: {{ serverInstallInfo.message }}
                    </div>
                    <div v-if="serverInstallInfo.status === 'completed'" class="tw:text-xs tw:text-green-600 tw:mt-1">
                        {{ t('home.server_install_completed') }} {{ t('home.server_install_duration') }}: {{ (serverInstallInfo.duration / 1000).toFixed(2) }}s
                    </div>
                    <div v-if="serverInstallInfo.status === 'error'" class="tw:text-xs tw:text-red-600 tw:mt-1 tw:break-words">
                        {{ t('home.server_install_error') }}: {{ serverInstallInfo.error }}
                    </div>
                </div>
                <div v-if="filterModsProgress.display" class="tw:mb-4">
                    <h1 class="tw:text-sm">{{ t('home.filter_mods_progress') }}</h1>
                    <a-progress :percent="filterModsProgress.percent" :status="filterModsProgress.status" size="small" />
                    <div v-if="filterModsInfo.totalMods > 0" class="tw:text-xs tw:text-gray-500 tw:mt-1">
                        {{ t('home.filter_mods_total') }}: {{ filterModsInfo.totalMods }}
                    </div>
                    <div v-if="filterModsInfo.modName" class="tw:text-xs tw:text-gray-600 tw:mt-1 tw:break-words">
                        {{ t('home.filter_mods_current') }}: {{ filterModsInfo.modName }}
                    </div>
                    <div v-if="filterModsInfo.status === 'completed'" class="tw:text-xs tw:text-green-600 tw:mt-1">
                        {{ t('home.filter_mods_completed', { filtered: filterModsInfo.filteredCount, moved: filterModsInfo.movedCount }) }}
                    </div>
                    <div v-if="filterModsInfo.status === 'error'" class="tw:text-xs tw:text-red-600 tw:mt-1 tw:break-words">
                        {{ t('home.filter_mods_error') }}: {{ filterModsInfo.error }}
                    </div>
                </div>
                <div v-if="optimizationReport" class="tw:mb-2">
                    <h1 class="tw:text-sm">{{ t('setting.category_optimize') }}</h1>
                    <div class="tw:text-xs tw:text-gray-500 tw:mt-1">
                        {{ t('setting.optimize_report_memory') }}: {{ optimizationReport.memoryMb }} MB
                        <span v-if="optimizationReport.highMemoryProfile" class="tw:text-amber-600 tw:ml-1">&gt;12GB</span>
                    </div>
                    <div class="tw:text-xs tw:text-gray-600 tw:mt-1">
                        {{ t('setting.optimize_report_removed') }}: {{ optimizationReport.removedClientResources?.length || 0 }}
                    </div>
                    <div v-if="optimizationReport.sanitizedSensitiveFiles?.length" class="tw:text-xs tw:text-amber-600 tw:mt-1">
                        {{ t('setting.optimize_report_sensitive') }}: {{ optimizationReport.sanitizedSensitiveFiles.length }}
                    </div>
                    <div v-if="optimizationReport.generatedScripts?.length" class="tw:text-xs tw:text-green-600 tw:mt-1">
                        {{ t('setting.optimize_report_generated_scripts') }}: {{ optimizationReport.generatedScripts.length }}
                    </div>
                </div>
            </a-card>
        </div>
        
        <a-modal v-model:open="showTemplateModal" :title="t('home.template_select_title')" :footer="null" width="700px">
            <a-spin :spinning="loadingTemplates">
                <div class="tw:mb-4">
                    <p class="tw:mb-2 tw:text-gray-600">{{ t('home.template_select_desc') }}</p>
                    <!-- 导入模板 -->
                    <!-- <a-upload-dragger name="file" action="/" :multiple="false" :before-upload="beforeUpload" @change="handleImportTemplateChange" accept=".zip">
                        <p class="ant-upload-drag-icon">
                            <inbox-outlined></inbox-outlined>
                        </p>
                        <p class="ant-upload-text">{{ t('home.template_import_title') }}</p>
                        <p class="ant-upload-hint">
                            {{ t('home.template_import_hint') }}
                        </p>
                    </a-upload-dragger> -->
                </div>
                
                <div class="tw:max-h-96 tw:overflow-y-auto tw:pr-2">
                    <div class="tw:grid tw:grid-cols-2 tw:gap-3">
                        <div 
                            @click="selectTemplate('0')"
                            :class="[
                                'tw:p-3 tw:rounded-lg tw:cursor-pointer tw:border-2 tw:transition-all tw:tw:h-32 tw:flex tw:flex-col tw:justify-between',
                                selectedTemplate === '0' ? 'tw:border-blue-500 tw:bg-blue-50' : 'tw:border-gray-200 hover:tw:border-gray-300'
                            ]"
                        >
                            <div>
                                <h3 class="tw:text-base tw:font-semibold tw:mb-1">{{ t('home.template_official_loader') }}</h3>
                                <p class="tw:text-xs tw:text-gray-600 tw:line-clamp-2">{{ t('home.template_official_loader_desc') }}</p>
                            </div>
                        </div>
                        
                        <div 
                            v-for="template in templates" 
                            :key="template.id"
                            @click="selectTemplate(template.id)"
                            :class="[
                                'tw:p-3 tw:rounded-lg tw:cursor-pointer tw:border-2 tw:transition-all tw:h-32 tw:flex tw:flex-col tw:justify-between',
                                selectedTemplate === template.id ? 'tw:border-blue-500 tw:bg-blue-50' : 'tw:border-gray-200 hover:tw:border-gray-300'
                            ]"
                        >
                            <div class="tw:flex-1 tw:overflow-hidden">
                                <div class="tw:flex tw:justify-between tw:items-start tw:mb-1">
                                    <h3 class="tw:text-base tw:font-semibold tw:truncate tw:flex-1">{{ template.metadata.name }}</h3>
                                    <!-- <a-button size="small" type="link" @click.stop="exportTemplate(template.id)">
                                        {{ t('home.template_export_button') }}
                                    </a-button> -->
                                </div>
                                <p class="tw:text-xs tw:text-gray-600 tw:line-clamp-2 tw:mb-2">{{ template.metadata.description }}</p>
                            </div>
                            <div class="tw:flex tw:justify-between tw:text-xs tw:text-gray-500 tw:mt-1">
                                <span class="tw:truncate tw:max-w-[50%]">{{ template.metadata.author }}</span>
                                <a-tag color="blue" size="small" class="tw:text-xs tw:px-1 tw:py-0.5 tw:truncate tw:max-w-[45%]">{{ template.metadata.version }}</a-tag>
                            </div>
                        </div>
                    </div>
                    
                    <div v-if="templates.length === 0 && !loadingTemplates" class="tw:text-center tw:py-8 tw:text-gray-500">
                        {{ t('template.empty') }}
                    </div>
                </div>
            </a-spin>
        </a-modal>

        <!-- 导出优化报告 -->
        <a-modal
            v-model:open="optimizationReportVisible"
            :title="t('setting.optimize_preview_title')"
            :footer="null"
            width="760px"
        >
            <div v-if="optimizationReport" class="tw:max-h-[520px] tw:overflow-auto tw:pr-1">
                <div class="tw:flex tw:items-center tw:gap-2 tw:flex-wrap tw:mb-3">
                    <a-tag v-if="optimizationReport.dryRun" color="blue">{{ t('setting.optimize_report_dryrun_badge') }}</a-tag>
                    <a-tag>{{ t('setting.optimize_report_memory') }}: {{ optimizationReport.memoryMb }} MB</a-tag>
                    <a-tag v-if="optimizationReport.highMemoryProfile" color="orange">&gt;12GB</a-tag>
                    <a-tag>{{ optimizationReport.elapsedMs }} ms</a-tag>
                </div>

                <a-descriptions bordered size="small" :column="1" class="tw:mb-3">
                    <a-descriptions-item :label="t('setting.optimize_report_generated_scripts')">
                        {{ reportList(optimizationReport.generatedScripts).join(', ') || t('setting.optimize_report_none') }}
                    </a-descriptions-item>
                    <a-descriptions-item :label="t('setting.optimize_report_removed')">
                        {{ optimizationReport.removedClientResources?.length || 0 }}
                    </a-descriptions-item>
                    <a-descriptions-item :label="t('setting.optimize_report_sensitive')">
                        {{ optimizationReport.sanitizedSensitiveFiles?.length || 0 }}
                    </a-descriptions-item>
                    <a-descriptions-item :label="t('setting.optimize_report_redundancy')">
                        {{ optimizationReport.redundancies?.length || 0 }}
                    </a-descriptions-item>
                    <a-descriptions-item v-if="optimizationReport.backupDirectory" :label="t('setting.optimize_report_backup')">
                        <span class="tw:break-all tw:text-xs">{{ optimizationReport.backupDirectory }}</span>
                    </a-descriptions-item>
                </a-descriptions>

                <template v-for="group in [
                    { title: t('setting.optimize_report_gains'), key: 'expectedGains' },
                    { title: t('setting.optimize_report_suggestions'), key: 'suggestions' },
                    { title: t('setting.optimize_report_warnings'), key: 'warnings' }
                ]" :key="group.key">
                    <div v-if="reportList(optimizationReport[group.key]).length > 0" class="tw:mb-3">
                        <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ group.title }}</p>
                        <ul class="tw:list-disc tw:pl-5 tw:text-xs tw:text-gray-600 tw:space-y-1">
                            <li v-for="(text, i) in reportList(optimizationReport[group.key])" :key="`${group.key}-${i}`">{{ text }}</li>
                        </ul>
                    </div>
                </template>

                <div v-if="optimizationReport.removedClientResources?.length" class="tw:mb-3">
                    <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ t('setting.optimize_report_removed') }}</p>
                    <div class="tw:max-h-40 tw:overflow-auto tw:rounded-lg tw:border tw:border-gray-100 tw:bg-slate-50 tw:p-2 tw:font-mono tw:text-[11px]">
                        <div v-for="(path, i) in optimizationReport.removedClientResources" :key="`rm-${i}`" class="tw:break-all tw:text-gray-600">{{ path }}</div>
                    </div>
                </div>

                <div v-if="optimizationReport.sanitizedSensitiveFiles?.length" class="tw:mb-3">
                    <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ t('setting.optimize_report_sensitive') }}</p>
                    <div class="tw:max-h-40 tw:overflow-auto tw:rounded-lg tw:border tw:border-amber-100 tw:bg-amber-50/50 tw:p-2 tw:font-mono tw:text-[11px]">
                        <div v-for="(path, i) in optimizationReport.sanitizedSensitiveFiles" :key="`sn-${i}`" class="tw:break-all tw:text-amber-700">{{ path }}</div>
                    </div>
                </div>

                <div v-if="optimizationReport.configChanges?.length" class="tw:mb-3">
                    <p class="tw:text-sm tw:font-semibold tw:text-gray-700 tw:mb-1">{{ t('setting.optimize_report_config_changes') }}</p>
                    <a-table
                        :data-source="optimizationReport.configChanges"
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
        </a-modal>
    </div>

</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.4s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>