<script lang="ts" setup>
import { ref, inject, watch, onUnmounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { message, notification } from 'ant-design-vue';
import { sendNotification } from '@tauri-apps/plugin-notification';
import { ErrorCode, createErrorInfo } from '../utils/errorCodes';
import { getSocketIO, disconnectSocket } from '../utils/socket';

const { t } = useI18n();

interface Props {
    file: File | undefined;
    mode: string;
    showSteps: boolean;
}

const props = defineProps<Props>();

const emit = defineEmits<{
    'step-change': [step: number];
    'reset': [];
}>();

const currentStep = ref(0);
const startTime = ref<number>(0);
const javaAvailable = ref(true);
const abortController = ref<AbortController | null>(null);

interface ProgressStatus {
    status: 'active' | 'success' | 'exception' | 'normal';
    percent: number;
    display: boolean;
    uploadedSize?: number;
    totalSize?: number;
    speed?: number;
    remainingTime?: number;
}

const uploadProgress = ref<ProgressStatus>({ status: 'active', percent: 0, display: false });
const unzipProgress = ref<ProgressStatus>({ status: 'active', percent: 0, display: true });
const downloadProgress = ref<ProgressStatus>({ status: 'active', percent: 0, display: true });
const downloadDescription = ref('');
const serverInstallProgress = ref<ProgressStatus>({ status: 'active', percent: 0, display: false });
const filterModsProgress = ref<ProgressStatus>({ status: 'active', percent: 0, display: false });

const serverInstallInfo = ref({
    modpackName: '',
    minecraftVersion: '',
    loaderType: '',
    loaderVersion: '',
    currentStep: '',
    stepIndex: 0,
    totalSteps: 0,
    message: '',
    status: 'idle' as 'idle' | 'installing' | 'completed' | 'error',
    error: '',
    installPath: '',
    duration: 0
});

const filterModsInfo = ref({
    totalMods: 0,
    currentMod: 0,
    modName: '',
    filteredCount: 0,
    movedCount: 0,
    status: 'idle' as 'idle' | 'filtering' | 'completed' | 'error',
    error: '',
    duration: 0
});

function resetState() {
    if (abortController.value) {
        abortController.value.abort();
        abortController.value = null;
    }
    
    disconnectSocket();
    
    uploadProgress.value = { status: 'active', percent: 0, display: false };
    unzipProgress.value = { status: 'active', percent: 0, display: true };
    downloadProgress.value = { status: 'active', percent: 0, display: true };
    downloadDescription.value = '';
    serverInstallProgress.value = { status: 'active', percent: 0, display: false };
    filterModsProgress.value = { status: 'active', percent: 0, display: false };
    currentStep.value = 0;
    
    const killCoreProcess = inject("killCoreProcess");
    if (killCoreProcess && typeof killCoreProcess === 'function') {
        killCoreProcess();
    }
}

watch(() => props.showSteps, (newVal) => {
    if (newVal && props.file) {
        runDeEarthX(props.file);
    }
});

async function runDeEarthX(file: File) {
    message.success(t('home.start_production'));

    const formData = new FormData();
    formData.append('file', file);

    try {
        message.loading(t('home.task_preparing'));
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        const url = `http://${apiHost}:${apiPort}/start?mode=${props.mode}`;

        uploadProgress.value = { status: 'active', percent: 0, display: true };
        startTime.value = Date.now();
        
        abortController.value = new AbortController();

        const response = await fetch(url, {
            method: 'POST',
            body: formData,
            signal: abortController.value.signal
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        uploadProgress.value.status = 'success';
        uploadProgress.value.percent = 100;
        setTimeout(() => {
            uploadProgress.value.display = false;
        }, 2000);

        message.success(t('home.task_connecting'));
        setupSocketIO();
    } catch (error: any) {
        if (error.name !== 'AbortError') {
            console.error('请求失败:', error);
            message.error(t('home.request_failed'));
            uploadProgress.value.status = 'exception';
        }
        resetState();
    }
}

function setupSocketIO() {
    message.loading(t('home.ws_connecting'));
    
    const socket = getSocketIO();

    socket.on('connect', () => {
        message.success(t('home.ws_connected'));
    });

    socket.on('error', (data: any) => {
        try {
            const parsed = typeof data === 'string' ? JSON.parse(data) : data;
            handleError(parsed.message);
        } catch {
            handleError(data);
        }
    });

    socket.on('info', (data: any) => {
        try {
            const parsed = typeof data === 'string' ? JSON.parse(data) : data;
            if (parsed.message) {
                message.info(parsed.message);
            }
        } catch {
            message.info(data);
        }
    });

    socket.on('changed', () => {
        currentStep.value++;
        emit('step-change', currentStep.value);
    });

    socket.on('unzip', (data: any) => {
        updateUnzipProgress(data);
    });

    socket.on('downloading', (data: any) => {
        updateDownloadProgress(data);
    });

    socket.on('finish', (data: any) => {
        handleFinish(data);
    });

    socket.on('server_install_start', (data: any) => {
        handleServerInstallStart(data);
    });

    socket.on('server_install_step', (data: any) => {
        handleServerInstallStep(data);
    });

    socket.on('server_install_progress', (data: any) => {
        handleServerInstallProgress(data);
    });

    socket.on('server_install_complete', (data: any) => {
        handleServerInstallComplete(data);
    });

    socket.on('server_install_error', (data: any) => {
        handleServerInstallError(data);
    });

    socket.on('filter_mods_start', (data: any) => {
        handleFilterModsStart(data);
    });

    socket.on('filter_mods_progress', (data: any) => {
        handleFilterModsProgress(data);
    });

    socket.on('filter_mods_complete', (data: any) => {
        handleFilterModsComplete(data);
    });

    socket.on('filter_mods_error', (data: any) => {
        handleFilterModsError(data);
    });

    socket.on('disconnect', () => {
        console.log('Socket.IO 连接关闭');
    });

    socket.on('connect_error', () => {
  notification.error({
    message: t('home.ws_error_title'),
    description: `${t('home.ws_error_desc')}\n\n${t('home.suggestions')}:\n1. ${t('home.suggestion_check_backend')}\n2. ${t('home.suggestion_check_port')}\n3. ${t('home.suggestion_restart_application')}`,
    duration: 0
  });
  resetState();
});
}

onUnmounted(() => {
    resetState();
});

function handleError(result: any) {
    if (result === 'jini') {
        javaAvailable.value = false;
        const errorInfo = createErrorInfo(ErrorCode.JAVA_NOT_FOUND);
        notification.error({
            message: `${t('home.java_error_title')} (${errorInfo.code})`,
            description: `${t('home.java_error_desc')}\n\n${t('home.suggestions')}:\n${errorInfo.suggestions?.map((s, i) => `${i + 1}. ${s}`).join('\n')}`,
            duration: 0
        });
    } else if (typeof result === 'string') {
        let errorInfo;

        if (result.includes('network') || result.includes('connection') || result.includes('timeout')) {
            errorInfo = createErrorInfo(ErrorCode.NETWORK_ERROR, result);
        } else if (result.includes('file') || result.includes('permission') || result.includes('disk')) {
            if (result.includes('not found')) {
                errorInfo = createErrorInfo(ErrorCode.FILE_NOT_FOUND, result);
            } else if (result.includes('permission')) {
                errorInfo = createErrorInfo(ErrorCode.FILE_PERMISSION_ERROR, result);
            } else if (result.includes('format')) {
                errorInfo = createErrorInfo(ErrorCode.FILE_FORMAT_ERROR, result);
            } else {
                errorInfo = createErrorInfo(ErrorCode.FILE_NOT_FOUND, result);
            }
        } else if (result.includes('memory') || result.includes('out of memory') || result.includes('heap')) {
            errorInfo = createErrorInfo(ErrorCode.MEMORY_INSUFFICIENT, result);
        } else if (result.includes('disk') || result.includes('space')) {
            errorInfo = createErrorInfo(ErrorCode.DISK_SPACE_INSUFFICIENT, result);
        } else {
            errorInfo = createErrorInfo(ErrorCode.UNKNOWN_ERROR, result);
        }

        const fullDescription = `${errorInfo.message}: ${errorInfo.details}\n\n${t('home.suggestions')}:\n${errorInfo.suggestions?.map((s, i) => `${i + 1}. ${s}`).join('\n')}`;

        notification.error({
            message: `${errorInfo.message} (${errorInfo.code})`,
            description: fullDescription,
            duration: 0
        });

        resetState();
    } else {
        const errorInfo = createErrorInfo(ErrorCode.UNKNOWN_ERROR);
        notification.error({
            message: `${t('home.unknown_error_title')} (${errorInfo.code})`,
            description: `${t('home.unknown_error_desc')}\n\n${t('home.suggestions')}:\n${errorInfo.suggestions?.map((s, i) => `${i + 1}. ${s}`).join('\n')}`,
            duration: 0
        });
        resetState();
    }
}

function updateUnzipProgress(result: { current: number; total: number }) {
    unzipProgress.value.percent = Math.round((result.current / result.total) * 100);
    if (result.current === result.total) {
        unzipProgress.value.status = 'success';
        setTimeout(() => {
            unzipProgress.value.display = false;
        }, 2000);
    }
    updateWindowProgress();
}

function updateDownloadProgress(result: { index: number; total: number; name?: string }) {
    downloadProgress.value.percent = Math.round((result.index / result.total) * 100);
    if (result.name) {
        downloadDescription.value = result.name;
    }
    if (downloadProgress.value.percent === 100) {
        downloadProgress.value.status = 'success';
        setTimeout(() => {
            downloadProgress.value.display = false;
        }, 2000);
    }
    updateWindowProgress();
}

function updateWindowProgress() {
    (window as any).progressData = {
        uploadProgress: uploadProgress.value,
        unzipProgress: unzipProgress.value,
        downloadProgress: downloadProgress.value,
        downloadDescription: downloadDescription.value,
        serverInstallProgress: serverInstallProgress.value,
        filterModsProgress: filterModsProgress.value,
        serverInstallInfo: serverInstallInfo.value,
        filterModsInfo: filterModsInfo.value
    };
}

function handleFinish(result: number) {
    const timeSpent = Math.round(result / 1000);
    currentStep.value++;
    emit('step-change', currentStep.value);
    message.success(t('home.production_complete', { time: timeSpent }));
    sendNotification({ title: t('common.app_name'), body: t('home.production_complete', { time: timeSpent }) });

    setTimeout(() => {
        emit('reset');
    }, 8000);
}

function handleServerInstallStart(result: any) {
    serverInstallInfo.value = {
        modpackName: result.modpackName,
        minecraftVersion: result.minecraftVersion,
        loaderType: result.loaderType,
        loaderVersion: result.loaderVersion,
        currentStep: '',
        stepIndex: 0,
        totalSteps: 0,
        message: 'Starting installation...',
        status: 'installing',
        error: '',
        installPath: '',
        duration: 0
    };
    serverInstallProgress.value = { status: 'active', percent: 0, display: true };
    updateWindowProgress();
}

function handleServerInstallStep(result: any) {
    serverInstallInfo.value.currentStep = result.step;
    serverInstallInfo.value.stepIndex = result.stepIndex;
    serverInstallInfo.value.totalSteps = result.totalSteps;
    serverInstallInfo.value.message = result.message || result.step;
    
    const overallProgress = (result.stepIndex / result.totalSteps) * 100;
    serverInstallProgress.value.percent = Math.round(overallProgress);
    updateWindowProgress();
}

function handleServerInstallProgress(result: any) {
    serverInstallInfo.value.currentStep = result.step;
    serverInstallInfo.value.message = result.message || result.step;
    serverInstallProgress.value.percent = result.progress;
    updateWindowProgress();
}

function handleServerInstallComplete(result: any) {
    serverInstallInfo.value.status = 'completed';
    serverInstallInfo.value.installPath = result.installPath;
    serverInstallInfo.value.duration = result.duration;
    serverInstallInfo.value.message = t('home.server_install_completed');
    serverInstallProgress.value = { status: 'success', percent: 100, display: true };
    
    currentStep.value++;
    emit('step-change', currentStep.value);
    
    const timeSpent = Math.round(result.duration / 1000);
    message.success(t('home.server_install_completed') + ` ${t('home.server_install_duration')}: ${timeSpent}s`);
    sendNotification({ title: t('common.app_name'), body: t('home.production_complete', { time: timeSpent }) });
    
    setTimeout(() => {
        serverInstallProgress.value.display = false;
    }, 8000);
    updateWindowProgress();
}

function handleServerInstallError(result: any) {
    serverInstallInfo.value.status = 'error';
    serverInstallInfo.value.error = result.error;
    serverInstallInfo.value.message = result.error;
    serverInstallProgress.value = { status: 'exception', percent: serverInstallProgress.value.percent, display: true };
    
    notification.error({
        message: t('home.server_install_error'),
        description: result.error,
        duration: 0
    });
    updateWindowProgress();
}

function handleFilterModsStart(result: any) {
    filterModsInfo.value = {
        totalMods: result.totalMods,
        currentMod: 0,
        modName: '',
        filteredCount: 0,
        movedCount: 0,
        status: 'filtering',
        error: '',
        duration: 0
    };
    filterModsProgress.value = { status: 'active', percent: 0, display: true };
    updateWindowProgress();
}

function handleFilterModsProgress(result: any) {
    filterModsInfo.value.currentMod = result.current;
    filterModsInfo.value.modName = result.modName;
    
    const percent = Math.round((result.current / result.total) * 100);
    filterModsProgress.value.percent = percent;
    updateWindowProgress();
}

function handleFilterModsComplete(result: any) {
    filterModsInfo.value.status = 'completed';
    filterModsInfo.value.filteredCount = result.filteredCount;
    filterModsInfo.value.movedCount = result.movedCount;
    filterModsInfo.value.duration = result.duration;
    filterModsProgress.value = { status: 'success', percent: 100, display: true };
    
    const timeSpent = Math.round(result.duration / 1000);
    message.success(t('home.filter_mods_completed', { filtered: result.filteredCount, moved: result.movedCount }) + ` ${t('home.server_install_duration')}: ${timeSpent}s`);
    
    setTimeout(() => {
        filterModsProgress.value.display = false;
    }, 8000);
    updateWindowProgress();
}

function handleFilterModsError(result: any) {
    filterModsInfo.value.status = 'error';
    filterModsInfo.value.error = result.error;
    filterModsProgress.value = { status: 'exception', percent: filterModsProgress.value.percent, display: true };
    
    notification.error({
        message: t('home.filter_mods_error'),
        description: result.error,
        duration: 0
    });
    updateWindowProgress();
}
</script>

<template>
</template>
