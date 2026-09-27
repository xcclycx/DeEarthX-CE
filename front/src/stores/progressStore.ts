import { reactive, readonly } from 'vue';
import { io, Socket } from 'socket.io-client';

export interface ProgressStatus {
    status: 'active' | 'success' | 'exception' | 'normal';
    percent: number;
    display: boolean;
    uploadedSize?: number;
    totalSize?: number;
    speed?: number;
    remainingTime?: number;
}

export interface ServerInstallInfo {
    modpackName: string;
    minecraftVersion: string;
    loaderType: string;
    loaderVersion: string;
    currentStep: string;
    stepIndex: number;
    totalSteps: number;
    message: string;
    status: 'idle' | 'installing' | 'completed' | 'error';
    error: string;
    installPath: string;
    duration: number;
}

export interface FilterModsInfo {
    totalMods: number;
    currentMod: number;
    modName: string;
    filteredCount: number;
    movedCount: number;
    status: 'idle' | 'filtering' | 'completed' | 'error';
    error: string;
    duration: number;
}

interface ProgressState {
    isMaking: boolean;
    showSteps: boolean;
    currentStep: number;
    uploadProgress: ProgressStatus;
    unzipProgress: ProgressStatus;
    downloadProgress: ProgressStatus;
    downloadDescription: string;
    downloadCompleted: number;
    downloadTotal: number;
    serverInstallProgress: ProgressStatus;
    serverInstallInfo: ServerInstallInfo;
    filterModsProgress: ProgressStatus;
    filterModsInfo: FilterModsInfo;
    startTime: number;
}

const state = reactive<ProgressState>({
    isMaking: false,
    showSteps: false,
    currentStep: 0,
    uploadProgress: { status: 'active', percent: 0, display: true },
    unzipProgress: { status: 'active', percent: 0, display: true },
    downloadProgress: { status: 'active', percent: 0, display: true },
    downloadDescription: '',
    downloadCompleted: 0,
    downloadTotal: 0,
    serverInstallProgress: { status: 'active', percent: 0, display: false },
    serverInstallInfo: {
        modpackName: '',
        minecraftVersion: '',
        loaderType: '',
        loaderVersion: '',
        currentStep: '',
        stepIndex: 0,
        totalSteps: 0,
        message: '',
        status: 'idle',
        error: '',
        installPath: '',
        duration: 0
    },
    filterModsProgress: { status: 'active', percent: 0, display: false },
    filterModsInfo: {
        totalMods: 0,
        currentMod: 0,
        modName: '',
        filteredCount: 0,
        movedCount: 0,
        status: 'idle',
        error: '',
        duration: 0
    },
    startTime: 0,
});

// Socket management - lives outside component lifecycle
let socket: Socket | null = null;

export function getProgressSocket(): Socket {
    if (socket && socket.connected) {
        return socket;
    }

    const host = import.meta.env.VITE_API_HOST || 'localhost';
    const port = import.meta.env.VITE_API_PORT || 37019;

    socket = io(`http://${host}:${port}`, {
        path: '/socket.io',
        transports: ['websocket'],
        reconnection: true,
        reconnectionAttempts: 5,
        reconnectionDelay: 1000,
        timeout: 20000,
        autoConnect: true,
        forceNew: true,
    });

    socket.on('connect', () => {
        console.log('Socket.IO 已连接 (store):', socket?.id);
    });

    socket.on('disconnect', (reason) => {
        console.log('Socket.IO 已断开 (store):', reason);
    });

    socket.on('connect_error', (error) => {
        console.error('Socket.IO 连接错误 (store):', error.message);
    });

    return socket;
}

export function disconnectProgressSocket(): void {
    if (socket) {
        socket.removeAllListeners();
        socket.disconnect();
        socket = null;
    }
}

export function getExistingSocket(): Socket | null {
    return socket;
}

// Reset all progress state
export function resetProgressState(): void {
    state.isMaking = false;
    state.showSteps = false;
    state.currentStep = 0;
    state.uploadProgress = { status: 'active', percent: 0, display: true };
    state.unzipProgress = { status: 'active', percent: 0, display: true };
    state.downloadProgress = { status: 'active', percent: 0, display: true };
    state.downloadDescription = '';
    state.downloadCompleted = 0;
    state.downloadTotal = 0;
    state.serverInstallProgress = { status: 'active', percent: 0, display: false };
    state.serverInstallInfo = {
        modpackName: '',
        minecraftVersion: '',
        loaderType: '',
        loaderVersion: '',
        currentStep: '',
        stepIndex: 0,
        totalSteps: 0,
        message: '',
        status: 'idle',
        error: '',
        installPath: '',
        duration: 0
    };
    state.filterModsProgress = { status: 'active', percent: 0, display: false };
    state.filterModsInfo = {
        totalMods: 0,
        currentMod: 0,
        modName: '',
        filteredCount: 0,
        movedCount: 0,
        status: 'idle',
        error: '',
        duration: 0
    };
    state.startTime = 0;
    disconnectProgressSocket();
}

export function getProgressState() {
    return state;
}

export const progressState = readonly(state) as Readonly<ProgressState>;