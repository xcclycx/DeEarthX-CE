<script lang="ts" setup>
import { h, provide, ref, onMounted, onUnmounted, computed, watch } from 'vue';
import { MenuProps, message } from 'ant-design-vue';
import { SettingOutlined, UploadOutlined, UserOutlined, WindowsOutlined, LoadingOutlined, FileSearchOutlined, FolderOutlined, AppstoreOutlined, CloudDownloadOutlined } from '@ant-design/icons-vue';
import { useRouter, useRoute } from 'vue-router';
import { Command } from '@tauri-apps/plugin-shell';
import { useI18n } from 'vue-i18n';
import { ErrorCode, createErrorInfo } from './utils/errorCodes';
import { getProgressState } from './stores/progressStore';
import { showAds } from './stores/settingsStore';
import TitleBar from './components/TitleBar.vue';
import ProcessErrorBell from './components/ProcessErrorBell.vue';
import { pluginApi } from './utils/pluginApi';

const router = useRouter();
const route = useRoute();
let killCoreProcess: (() => void) | null = null;

const { t } = useI18n();

// 版本号相关
const version = ref<string>('V4');

// 加载版本号
async function loadVersion() {
    try {
        console.log('开始加载版本号...');
        const response = await fetch('/version.json');
        console.log('version.json 响应状态:', response.status);
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        const data = await response.json();
        console.log('版本号数据:', data);
        version.value = `V${data.version}`;
        console.log('设置版本号为:', version.value);
    } catch (error) {
        console.error('加载版本号失败:', error);
        version.value = 'V4';
    }
}

// 后端连接状态相关
const backendStatus = ref<'loading' | 'success' | 'error'>('loading');
const backendErrorInfo = ref<string>('');
const retryCount = ref<number>(0);
const maxRetries = 5;

// 检测端口是否被正确的后端占用
async function checkPortOccupied(): Promise<'correct_backend' | 'wrong_app' | 'free'> {
    try {
        const response = await fetch("http://localhost:37019/config/get", {
            method: "GET",
            signal: AbortSignal.timeout(1000)
        });
        
        if (response.ok) {
            const config = await response.json();
            // 检查是否包含 DeEarthX 后端的特征字段（mirror、filter 等）
            if (config.mirror !== undefined || config.filter !== undefined) {
                // 端口被正确的后端占用
                return 'correct_backend';
            } else {
                // 端口被其他应用占用
                return 'wrong_app';
            }
        } else {
            return 'free';
        }
    } catch (error) {
        // 连接失败，端口可能是空闲的
        return 'free';
    }
}

// 启动后端核心服务
async function runCoreProcess() {
    // 先检测端口状态
    const portStatus = await checkPortOccupied();
    
    if (portStatus === 'correct_backend') {
        // 端口已经被正确的后端占用，直接使用
        backendStatus.value = 'success';
        backendErrorInfo.value = '';
        message.success(t('message.backend_running'));
        return;
    }

    if (portStatus === 'wrong_app') {
        // 端口被其他应用占用
        const errorInfo = createErrorInfo(ErrorCode.BACKEND_PORT_OCCUPIED);
        backendStatus.value = 'error';
        backendErrorInfo.value = `${errorInfo.message} (错误码: ${errorInfo.code})`;
        message.error(backendErrorInfo.value);
        router.push(`/error?e=${encodeURIComponent(backendErrorInfo.value)}&code=${errorInfo.code}`);
        return;
    }
    
    // 端口空闲，尝试启动后端
    backendStatus.value = 'loading';
    
    // 构建后端可执行文件路径
    let corePath = "core";
    
    // 在生产环境中，后端可执行文件应该在binaries目录中
    try {
        const { appDataDir } = await import('@tauri-apps/api/path');
        await appDataDir();
        // 简化处理，直接使用相对路径
        corePath = "core";
    } catch (error) {
        console.log('使用默认core路径:', error);
    }
    
    Command.create(corePath).spawn()
        .then((e) => {
            console.log("DeEarthX V4 Core");
            killCoreProcess = e.kill;

            // 等待后端就绪（轮询，见 waitForBackendReady）
            void waitForBackendReady();
        })
        .catch((error) => {
            console.error(error);
            retryCount.value++;
            
            if (retryCount.value <= maxRetries) {
                message.info(t('message.retry_start', { current: retryCount.value, max: maxRetries }));
                setTimeout(() => {
                    runCoreProcess();
                }, 2000);
            } else {
                const errorInfo = createErrorInfo(ErrorCode.BACKEND_START_FAILED, `已重试 ${maxRetries} 次`);
                backendStatus.value = 'error';
                backendErrorInfo.value = `${errorInfo.message} (错误码: ${errorInfo.code})`;
                message.error(backendErrorInfo.value);
                router.push(`/error?e=${encodeURIComponent(backendErrorInfo.value)}&code=${errorInfo.code}`);
            }
        });
}

/**
 * 等待后端就绪。
 *
 * 后端冷启动耗时并不稳定（UPX 解压、.NET 预热、Java 探测、首次建目录），
 * 原来只探测一次且仅等 3 秒：一旦错过就被判为启动失败，而健康检查只从
 * success 状态生效，于是应用会永久停在错误页无法自愈。
 * 改为在总时限内轮询，成功即返回。
 */
const BACKEND_READY_TIMEOUT_MS = 30000;
const BACKEND_READY_POLL_MS = 1000;

async function waitForBackendReady() {
    const deadline = Date.now() + BACKEND_READY_TIMEOUT_MS;
    let lastError = '未知错误';

    while (Date.now() < deadline) {
        try {
            const response = await fetch("http://localhost:37019/", {
                method: "GET",
                signal: AbortSignal.timeout(2000)
            });
            if (response.ok) {
                backendStatus.value = 'success';
                backendErrorInfo.value = '';
                message.success(t('message.backend_started'));
                return;
            }
            lastError = `HTTP 状态码: ${response.status}`;
        } catch (error) {
            lastError = error instanceof Error ? error.message : String(error);
        }
        await new Promise((resolve) => setTimeout(resolve, BACKEND_READY_POLL_MS));
    }

    console.error("后端连接失败:", lastError);
    const errorInfo = createErrorInfo(
        lastError.startsWith('HTTP 状态码') ? ErrorCode.BACKEND_RESPONSE_ERROR : ErrorCode.BACKEND_CONNECTION_FAILED,
        lastError
    );
    backendStatus.value = 'error';
    backendErrorInfo.value = `${errorInfo.message} (错误码: ${errorInfo.code})`;
    message.error(backendErrorInfo.value);
    router.push(`/error?e=${encodeURIComponent(backendErrorInfo.value)}&code=${errorInfo.code}`);
}


// 健康检查定时器
let healthCheckInterval: ReturnType<typeof setInterval> | null = null;

function startHealthCheck() {
    stopHealthCheck();
    healthCheckInterval = setInterval(async () => {
        // 'loading' 表示已有一轮启动/重启流程在进行，跳过以避免并发重启。
        // 处于 'error' 时继续往下走，让后端有机会自愈。
        if (backendStatus.value === 'loading') return;
        try {
            const response = await fetch('http://localhost:37019/', {
                method: 'GET',
                signal: AbortSignal.timeout(3000)
            });
            if (!response.ok) {
                throw new Error(`状态码: ${response.status}`);
            }
        } catch {
            console.warn('后端健康检查失败，尝试重启...');
            backendStatus.value = 'loading';
            message.warning(t('message.backend_health_check_failed_restart'));
            runCoreProcess();
        }
    }, 30000);
}

function stopHealthCheck() {
    if (healthCheckInterval !== null) {
        clearInterval(healthCheckInterval);
        healthCheckInterval = null;
    }
}

function handleVisibilityChange() {
    if (document.hidden) {
        stopHealthCheck();
        stopAdRotation();
    } else {
        startHealthCheck();
        if (sponsors.value.length > 1) {
            startAdRotation();
        }
    }
}

// 组件挂载时启动后端
onMounted(async () => {
    loadVersion();
    runCoreProcess();
    startHealthCheck();
    document.addEventListener('visibilitychange', handleVisibilityChange);
    fetchAds();
    startAdRotation();
});

// 插件侧边栏与注入资源依赖后端，等待后端就绪后再加载，
// 而不是固定等待 5 秒（既慢又不可靠）。
watch(backendStatus, (status) => {
    if (status === 'success') {
        void loadPluginAssets();
    }
});

let pluginAssetsLoaded = false;

async function loadPluginAssets() {
    if (pluginAssetsLoaded) return;
    pluginAssetsLoaded = true;

    // 侧边栏只需要清单，先出菜单；注入资源体积较大，放到下一帧再拉，
    // 避免和后端刚就绪时的其他请求抢带宽。
    await fetchPluginSidebarItems();
    requestAnimationFrame(() => {
        void loadPluginInjections();
    });
}

// 广告/赞助商相关
interface Sponsor {
    id: string;
    name: string;
    imageUrl: string;
    type: string;
    url: string;
}

const sponsors = ref<Sponsor[]>([]);
const currentAdIndex = ref(0);
let adRotationTimer: ReturnType<typeof setInterval> | null = null;
const SPONSORS_JSON_URL = "https://bk.xcclyc.cn/upzzs.json";

async function fetchAds() {
    try {
        const response = await fetch(SPONSORS_JSON_URL);
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        const data = await response.json();
        sponsors.value = data;
    } catch (error) {
        console.error("Failed to fetch sponsors:", error);
        sponsors.value = [
            {
                id: "elfidc",
                name: "亿讯云",
                imageUrl: "./elfidc.svg",
                type: t('about.sponsor_type_gold'),
                url: "https://www.elfidc.com"
            }
        ];
    }
}

function startAdRotation() {
    stopAdRotation();
    if (sponsors.value.length > 1) {
        adRotationTimer = setInterval(() => {
            currentAdIndex.value = (currentAdIndex.value + 1) % sponsors.value.length;
        }, 10000);
    }
}

function openSponsorUrl(url: string) {
    window.open(url, '_blank');
}

function stopAdRotation() {
    if (adRotationTimer !== null) {
        clearInterval(adRotationTimer);
        adRotationTimer = null;
    }
}

// 监听赞助商列表变化，重新启动轮播
watch(sponsors, () => {
    currentAdIndex.value = 0;
    startAdRotation();
});

// 正在制作的状态指示器（非主页时显示）
//
// 性能：步骤标题数组不再在 computed 里每次重建；进度文案只依赖
// 四个进度对象的原始字段，避免引用变化导致的重复计算。
const makingStepLabels = computed(() => [
    t('home.step1_title'),
    t('home.step2_title'),
    t('home.step3_title'),
    t('home.step4_title')
]);

const isMakingIndicator = computed(() => {
    const state = getProgressState();
    if (!state.isMaking) return null;
    const step = makingStepLabels.value[state.currentStep] || t('home.unknown_step');
    let progressText = '';
    if (state.uploadProgress.display && state.uploadProgress.percent < 100) {
        progressText = `${state.uploadProgress.percent}%`;
    } else if (state.unzipProgress.display && state.unzipProgress.percent < 100) {
        progressText = `${t('home.unzip_progress')}: ${state.unzipProgress.percent}%`;
    } else if (state.downloadProgress.display && state.downloadProgress.percent < 100) {
        progressText = `${t('home.download_progress')}: ${state.downloadProgress.percent}%`;
    } else if (state.serverInstallProgress.display && state.serverInstallProgress.percent < 100) {
        progressText = `${t('home.server_install_progress')}: ${state.serverInstallProgress.percent}%`;
    } else if (state.filterModsProgress.display && state.filterModsProgress.percent < 100) {
        progressText = `${t('home.filter_mods_progress')}: ${state.filterModsProgress.percent}%`;
    }
    return { step, progressText, isMaking: true };
});

provide("killCoreProcess", () => {
        if (killCoreProcess && typeof killCoreProcess === 'function') {
            killCoreProcess();
            killCoreProcess = null;
            message.info(t('message.backend_restart'));
            runCoreProcess();
        }
});

onUnmounted(() => {
    stopHealthCheck();
    stopAdRotation();
    document.removeEventListener('visibilitychange', handleVisibilityChange);
});

// 导航菜单配置
const selectedKeys = ref<(string | number)[]>(['main']);

interface PluginSidebarItem {
    key: string;
    pluginId: string;
    label: string;
    icon?: string;
    route: string;
}

const pluginSidebarItems = ref<PluginSidebarItem[]>([]);

async function fetchPluginSidebarItems() {
    try {
        const plugins = await pluginApi.list();
        const items: PluginSidebarItem[] = [];

        for (const plugin of plugins) {
            // 只认新能力分组；旧字段 hasSidebar 由后端兼容输出，因此两边都能命中。
            const hasSidebar = plugin.manifest?.capabilities?.sidebar ?? plugin.manifest?.hasSidebar;
            const sidebarItems = plugin.manifest?.sidebarItems ?? [];

            if (!plugin.enabled || !hasSidebar || sidebarItems.length === 0) {
                continue;
            }

            for (const item of sidebarItems) {
                items.push({
                    key: `plugin-${item.key}`,
                    pluginId: plugin.manifest.id,
                    label: item.label,
                    route: item.route.replace(/^\//, ''),
                    icon: item.icon
                });
            }
        }

        pluginSidebarItems.value = items;
    } catch {
        // 后端可能还未就绪，忽略
    }
}

async function loadPluginInjections() {
    try {
        const injections = await pluginApi.injections();
        const loadedUrls = new Set(
            Array.from(document.querySelectorAll('link[data-plugin-inject], script[data-plugin-inject]'))
                .map(el => el.getAttribute('data-plugin-inject'))
        );

        for (const injection of injections) {
            for (const cssUrl of injection.css) {
                if (!loadedUrls.has(cssUrl)) {
                    const link = document.createElement('link');
                    link.rel = 'stylesheet';
                    link.href = cssUrl;
                    link.setAttribute('data-plugin-inject', cssUrl);
                    document.head.appendChild(link);
                }
            }
            for (const jsUrl of injection.js) {
                if (!loadedUrls.has(jsUrl)) {
                    const script = document.createElement('script');
                    script.src = jsUrl;
                    script.setAttribute('data-plugin-inject', jsUrl);
                    document.head.appendChild(script);
                }
            }
        }
    } catch {
        // 后端可能还未就绪，忽略
    }
}

// 监听路由变化，更新选中菜单
router.beforeEach((to, _from, next) => {
    const routeToKey: Record<string, string> = {
        '/': 'main',
        '/setting': 'setting',
        '/about': 'about',
        '/error': 'main',
        '/galaxy': 'galaxy',
        '/deearth': 'deearth',
        '/template': 'template',
        '/download': 'download',
        '/plugins': 'plugin-manager',
        '/plugin': 'plugin-manager'
    };

    if (to.path.startsWith('/plugin/')) {
        selectedKeys.value[0] = 'plugin-manager';
    } else if (to.path.startsWith('/plugin-page/')) {
        const sidebarItem = pluginSidebarItems.value.find(item => to.path.includes(item.pluginId) && to.path.includes(item.route));
        if (sidebarItem) {
            selectedKeys.value[0] = sidebarItem.key;
        } else {
            selectedKeys.value[0] = 'plugin-manager';
        }
    } else {
        const matchedKey = routeToKey[to.path];
        if (matchedKey) {
            selectedKeys.value[0] = matchedKey;
        } else {
            const sidebarItem = pluginSidebarItems.value.find(item => to.path.startsWith(item.route));
            if (sidebarItem) {
                selectedKeys.value[0] = sidebarItem.key;
            } else {
                selectedKeys.value[0] = 'main';
            }
        }
    }
    next();
});

// 菜单项配置（使用计算属性使其响应语言变化）
const menuItems = computed<MenuProps['items']>(() => {
    return [
        {
            key: 'main',
            icon: h(WindowsOutlined),
            label: t('menu.home'),
            title: t('menu.home'),
        },
        {
            key: 'deearth',
            icon: h(FileSearchOutlined),
            label: t('menu.deearth'),
            title: t('menu.deearth'),
        },
        {
            key: 'galaxy',
            icon: h(UploadOutlined),
            label: t('menu.galaxy'),
            title: t('menu.galaxy'),
        },
        {
            key: 'template',
            icon: h(FolderOutlined),
            label: t('menu.template'),
            title: t('menu.template'),
        },
        {
            key: 'download',
            icon: h(CloudDownloadOutlined),
            label: t('menu.download'),
            title: t('menu.download'),
        },
        {
            key: 'plugin',
            icon: h(AppstoreOutlined),
            label: t('menu.plugin'),
            title: t('menu.plugin'),
            children: pluginSidebarItems.value.length > 0
                ? [
                    {
                        key: 'plugin-manager',
                        label: t('plugin.title'),
                        title: t('plugin.title'),
                    },
                    ...pluginSidebarItems.value.map(item => ({
                        key: item.key,
                        label: item.label,
                        title: item.label,
                    }))
                  ]
                : undefined,
        },
        {
            key: 'setting',
            icon: h(SettingOutlined),
            label: t('menu.setting'),
            title: t('menu.setting'),
        },
        {
            key: 'about',
            icon: h(UserOutlined),
            label: t('menu.about'),
            title: t('menu.about'),
        }
    ];
});

// 菜单点击事件处理
const handleMenuClick: MenuProps['onClick'] = (e) => {
    selectedKeys.value[0] = e.key;
    const routeMap: Record<string, string> = {
        main: '/',
        deearth: '/deearth',
        setting: '/setting',
        about: '/about',
        galaxy: '/galaxy',
        template: '/template',
        download: '/download',
        plugin: '/plugins',
        'plugin-manager': '/plugins'
    };
    // 检查是否插件侧边栏项
    const sidebarItem = pluginSidebarItems.value.find(item => item.key === e.key);
    if (sidebarItem) {
        router.push(`/plugin-page/${sidebarItem.pluginId}/${sidebarItem.route}`);
        return;
    }
    const route = routeMap[e.key] || '/';
    router.push(route);
};

// 主题配置
const theme = ref({
    token: {
        colorPrimary: '#67eac3',
        borderRadius: 8,
    },
    components: {
        Menu: {
            itemActiveBg: '#e8fff5',
            itemSelectedBg: '#e8fff5',
            itemSelectedColor: '#10b981',
        }
    }
});
</script>

<template>
    <a-config-provider :theme="theme">
        <div class="tw:h-screen tw:w-screen tw:flex tw:flex-col tw:overflow-hidden">
            <!-- 自定义标题栏 -->
            <TitleBar
                :version="version"
                :backendStatus="backendStatus"
                :backendErrorInfo="backendErrorInfo"
                :showAds="showAds"
            >
                <template #extra>
                    <!-- 程序错误提醒（含未读角标与详情抽屉） -->
                    <ProcessErrorBell />

                    <!-- 制作进度指示器 -->
                    <div
                        v-if="isMakingIndicator && route.path !== '/'"
                        class="tw:flex tw:items-center tw:gap-2 tw:px-3 tw:py-1 tw:bg-emerald-50 tw:border tw:border-emerald-200 tw:rounded-full tw:text-xs tw:text-emerald-700 tw:animate-pulse"
                    >
                        <LoadingOutlined style="color: #10b981; font-size: 14px;" />
                        <span class="tw:font-medium">{{ t('home.making_indicator') }}</span>
                        <span class="tw:text-gray-400">|</span>
                        <span>{{ t('home.making_step') }}: {{ isMakingIndicator.step }}</span>
                        <span v-if="isMakingIndicator.progressText" class="tw:text-gray-400">|</span>
                        <span v-if="isMakingIndicator.progressText">{{ t('home.making_progress') }}: {{ isMakingIndicator.progressText }}</span>
                    </div>
                    <!-- 广告位 -->
                    <div
                        v-if="showAds && sponsors.length > 0"
                        class="tw:flex tw:items-center tw:gap-2 tw:px-3 tw:py-1 tw:bg-amber-50 tw:border tw:border-amber-200 tw:rounded-full tw:cursor-pointer tw:hover:bg-amber-100 tw:transition-colors"
                        @mousedown.stop
                        @click="openSponsorUrl(sponsors[currentAdIndex]?.url || '')"
                        :title="sponsors[currentAdIndex]?.name"
                    >
                        <img
                            v-if="sponsors[currentAdIndex]?.imageUrl"
                            :src="sponsors[currentAdIndex].imageUrl"
                            :alt="sponsors[currentAdIndex].name"
                            class="tw:h-5 tw:max-w-[60px] tw:object-contain"
                        />
                        <span class="tw:text-xs tw:text-amber-700 tw:font-medium tw:whitespace-nowrap">
                            {{ sponsors[currentAdIndex]?.name }}
                        </span>
                        <span v-if="sponsors.length > 1" class="tw:text-xs tw:text-amber-400">
                            ({{ currentAdIndex + 1 }}/{{ sponsors.length }})
                        </span>
                    </div>
                </template>
            </TitleBar>

            <!-- 主体内容区域 -->
            <div class="tw:flex tw:flex-1 tw:overflow-hidden">
                <!-- 侧边菜单 -->
                <a-menu
                    id="menu"
                    class="tw:shadow-lg tw:z-20"
                    style="width: 220px; flex-shrink: 0;"
                    :selectedKeys="selectedKeys"
                    mode="inline"
                    :items="menuItems"
                    @click="handleMenuClick"
                />

                <!-- 内容区域 - 带过渡动画 -->
                <div class="tw:flex-1 tw:overflow-hidden tw:relative tw:bg-gradient-to-br tw:from-slate-50 tw:via-blue-50 tw:to-indigo-50">
                    <router-view v-slot="{ Component }">
                        <transition
                            name="fade-slide"
                            mode="out-in"
                            appear
                        >
                            <component :is="Component" :key="route.path" class="tw:w-full tw:h-full tw:absolute tw:top-0 tw:left-0" />
                        </transition>
                    </router-view>
                </div>
            </div>
        </div>
    </a-config-provider>
</template>

<style>
/* 禁止选择文本的样式 */
h1,
li,
p,
span {
    -webkit-user-select: none;
    -moz-user-select: none;
    -ms-user-select: none;
    user-select: none;
}

/* 禁止拖拽图片 */
img {
    -webkit-user-drag: none;
    -moz-user-drag: none;
    -ms-user-drag: none;
}

/* 页面切换过渡动画 - 淡入淡出 + 滑动 */
.fade-slide-enter-active {
    animation: fadeSlideIn 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

.fade-slide-leave-active {
    animation: fadeSlideOut 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

@keyframes fadeSlideIn {
    0% {
        opacity: 0;
        transform: translateX(20px);
    }
    100% {
        opacity: 1;
        transform: translateX(0);
    }
}

@keyframes fadeSlideOut {
    0% {
        opacity: 1;
        transform: translateX(0);
    }
    100% {
        opacity: 0;
        transform: translateX(-20px);
    }
}

/* 菜单项美化 */
#menu .ant-menu-item {
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    border-radius: 8px;
    margin: 8px 3px;
    height: 48px;
    display: flex;
    align-items: center;
    font-size: 14px;
    font-weight: 550;
    position: relative;
    overflow: hidden;
}

#menu .ant-menu-item::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    width: 4px;
    height: 100%;
    background: linear-gradient(180deg, #67eac3 0%, #10b981 100%);
    border-radius: 0 4px 4px 0;
    transform: scaleY(0);
    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

#menu .ant-menu-item:hover {
    transform: translateX(4px);
    background: #f0fdf9;
}

#menu .ant-menu-item-selected {
    background: linear-gradient(135deg, #d1fae5 0%, #e8fff5 100%);
    box-shadow: 0 4px 16px rgba(16, 185, 129, 0.2);
}

#menu .ant-menu-item-selected::before {
    transform: scaleY(1);
}

#menu .ant-menu-item-selected .anticon {
    color: #10b981;
    transform: scale(1.1);
    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

#menu .anticon {
    font-size: 16px;
    margin-right: 8px;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

#menu .ant-menu-item:hover .anticon {
    color: #10b981;
    transform: scale(1.1) rotate(5deg);
}

/* 滚动条美化 */
::-webkit-scrollbar {
    width: 8px;
    height: 8px;
}

::-webkit-scrollbar-track {
    background: #f1f5f9;
    border-radius: 4px;
}

::-webkit-scrollbar-thumb {
    background: linear-gradient(180deg, #94a3b8 0%, #64748b 100%);
    border-radius: 4px;
    transition: all 0.3s ease;
}

::-webkit-scrollbar-thumb:hover {
    background: linear-gradient(180deg, #64748b 0%, #475569 100%);
}
</style>