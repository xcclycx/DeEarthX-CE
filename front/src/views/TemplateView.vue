<script lang="ts" setup>
import { ref, onMounted, computed } from 'vue';
import { message } from 'ant-design-vue';
import { PlusOutlined, DeleteOutlined, FolderOutlined, ExclamationCircleOutlined, EditOutlined, UploadOutlined, DownloadOutlined } from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';

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

// 模板商店模板接口
interface StoreTemplate {
    id: string;
    name: string;
    description: string;
    size: string;
    downloadUrls: string[];
    version: string;
    tag: string;
}

const templates = ref<Template[]>([]);
const loading = ref(false);
const showCreateModal = ref(false);
const showDeleteModal = ref(false);
const showEditModal = ref(false);
const deletingTemplate = ref<Template | null>(null);
const editingTemplate = ref<Template | null>(null);

// 导出进度相关状态
const exportLoading = ref(false);
const exportProgress = ref(0);
const showExportProgress = ref(false);

// 导入进度相关状态
const importLoading = ref(false);
const importProgress = ref(0);
const showImportProgress = ref(false);

// 下载进度相关状态
const downloadLoading = ref(false);
const downloadProgress = ref(0);
const showDownloadProgress = ref(false);

// 下载状态管理
const downloadStates = ref<Map<string, { url: string, downloadedSize: number, totalSize: number }>>(new Map());

// 测试下载链接速度
async function testDownloadSpeed(urls: string[]): Promise<string> {
    const speedTests = urls.map(async (url) => {
        try {
            const startTime = performance.now();
            const response = await fetch(url, {
                method: 'HEAD'
            });
            if (response.ok) {
                const endTime = performance.now();
                return { url, time: endTime - startTime };
            }
            return { url, time: Infinity };
        } catch (error) {
            console.error(`测试链接 ${url} 失败:`, error);
            return { url, time: Infinity };
        }
    });
    
    const results = await Promise.all(speedTests);
    const fastest = results.sort((a, b) => a.time - b.time)[0];
    return fastest.url;
}

// 模板商店相关状态
const storeTemplates = ref<StoreTemplate[]>([]);
const storeLoading = ref(false);
const activeTab = ref('local'); // 'local' 或 'store'
const selectedTag = ref<string>('all'); // 'all', 'dex', 'CE'
const searchKeyword = ref('');

// 过滤后的模板
const filteredStoreTemplates = computed(() => {
    let filtered = storeTemplates.value;
    
    // 按标签筛选
    if (selectedTag.value !== 'all') {
        filtered = filtered.filter(template => template.tag === selectedTag.value);
    }
    
    // 按关键词搜索
    if (searchKeyword.value) {
        const keyword = searchKeyword.value.toLowerCase();
        filtered = filtered.filter(template => 
            template.name.toLowerCase().includes(keyword) ||
            template.description.toLowerCase().includes(keyword)
        );
    }
    
    return filtered;
});

const newTemplate = ref({
    name: '',
    version: '1.0.0',
    description: '',
    author: ''
});

async function loadTemplates() {
    loading.value = true;
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
        loading.value = false;
    }
}

function openCreateModal() {
    newTemplate.value = {
        name: '',
        version: '1.0.0',
        description: '',
        author: ''
    };
    showCreateModal.value = true;
}

async function createTemplate() {
    if (!newTemplate.value.name) {
        message.error(t('template.name_required'));
        return;
    }

    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        
        const response = await fetch(`http://${apiHost}:${apiPort}/templates`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(newTemplate.value)
        });
        
        const result = await response.json();
        
        if (result.status === 200) {
            message.success(t('template.create_success'));
            showCreateModal.value = false;
            await loadTemplates();
        } else {
            message.error(result.message || t('template.create_failed'));
        }
    } catch (error) {
        console.error('创建模板失败:', error);
        message.error(t('template.create_failed'));
    }
}

function openDeleteModal(template: Template) {
    deletingTemplate.value = template;
    showDeleteModal.value = true;
}

async function confirmDelete() {
    if (!deletingTemplate.value) return;

    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        
        const response = await fetch(`http://${apiHost}:${apiPort}/templates/${deletingTemplate.value.id}`, {
            method: 'DELETE'
        });
        
        const result = await response.json();
        
        if (result.status === 200) {
            message.success(t('template.delete_success'));
            showDeleteModal.value = false;
            await loadTemplates();
        } else {
            message.error(result.message || t('template.delete_failed'));
        }
    } catch (error) {
        console.error('删除模板失败:', error);
        message.error(t('template.delete_failed'));
    }
}

async function openTemplateFolder(template: Template) {
    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        
        const response = await fetch(`http://${apiHost}:${apiPort}/templates/${template.id}/path`);
        const result = await response.json();
        
        if (result.status !== 200) {
            message.error(result.message || t('template.open_folder_failed'));
        }
    } catch (error) {
        console.error('打开文件夹失败:', error);
        message.error(t('template.open_folder_failed'));
    }
}

function openEditModal(template: Template) {
    editingTemplate.value = template;
    newTemplate.value = {
        name: template.metadata.name,
        version: template.metadata.version,
        description: template.metadata.description,
        author: template.metadata.author
    };
    showEditModal.value = true;
}

async function updateTemplate() {
    if (!editingTemplate.value || !newTemplate.value.name) {
        message.error(t('template.name_required'));
        return;
    }

    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        
        const response = await fetch(`http://${apiHost}:${apiPort}/templates/${editingTemplate.value.id}`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(newTemplate.value)
        });
        
        const result = await response.json();
        
        if (result.status === 200) {
            message.success(t('template.update_success'));
            showEditModal.value = false;
            await loadTemplates();
        } else {
            message.error(result.message || t('template.update_failed'));
        }
    } catch (error) {
        console.error('更新模板失败:', error);
        message.error(t('template.update_failed'));
    }
}

// 导出模板
async function exportTemplate(templateId: string) {
    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        
        // 重置进度状态
        exportProgress.value = 0;
        exportLoading.value = true;
        showExportProgress.value = true;
        
        // 发送导出请求
        const response = await fetch(`http://${apiHost}:${apiPort}/templates/${templateId}/export`);
        
        if (response.ok) {
            // 获取文件名
            const contentDisposition = response.headers.get('content-disposition');
            let fileName = 'template.zip';
            if (contentDisposition) {
                const matches = /filename="([^"]+)"/.exec(contentDisposition);
                if (matches && matches[1]) {
                    fileName = matches[1];
                }
            }
            
            // 获取文件大小
            const contentLength = response.headers.get('content-length');
            const totalSize = contentLength ? parseInt(contentLength) : 0;
            
            // 创建读取器
            const reader = response.body?.getReader();
            if (!reader) {
                throw new Error('无法读取响应体');
            }
            
            // 存储数据
            const chunks: Uint8Array[] = [];
            let loadedSize = 0;
            
            // 读取数据并更新进度
            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                if (value) {
                    chunks.push(value);
                    loadedSize += value.length;
                    if (totalSize > 0) {
                        exportProgress.value = Math.round((loadedSize / totalSize) * 100);
                    }
                }
            }
            
            // 合并数据
            const blob = new Blob(chunks);
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            URL.revokeObjectURL(url);
            
            message.success(t('home.template_export_success'));
        } else {
            message.error(t('home.template_export_failed'));
        }
    } catch (error) {
        console.error('导出模板失败:', error);
        message.error(t('home.template_export_failed'));
    } finally {
        // 重置状态
        exportLoading.value = false;
        showExportProgress.value = false;
        exportProgress.value = 0;
    }
}

// 导入模板
function importTemplate(options: any) {
    const { file, onSuccess, onError } = options;
    
    // 重置进度状态
    importProgress.value = 0;
    importLoading.value = true;
    showImportProgress.value = true;
    
    const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
    const apiPort = import.meta.env.VITE_API_PORT || '37019';
    
    const formData = new FormData();
    formData.append('file', file);
    
    const xhr = new XMLHttpRequest();
    
    // 监听上传进度
    xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable) {
            const percentComplete = Math.round((event.loaded / event.total) * 100);
            importProgress.value = percentComplete;
        }
    });
    
    // 监听完成
    xhr.addEventListener('load', async () => {
        try {
            const result = JSON.parse(xhr.responseText);
            if (result.status === 200) {
                message.success(t('home.template_import_success'));
                // 重新加载模板列表
                await loadTemplates();
                if (onSuccess) onSuccess(result);
            } else {
                message.error(t('home.template_import_failed'));
                if (onError) onError(result);
            }
        } catch (error) {
            console.error('导入模板失败:', error);
            message.error(t('home.template_import_failed'));
            if (onError) onError(error);
        } finally {
            // 重置状态
            importLoading.value = false;
            showImportProgress.value = false;
            importProgress.value = 0;
        }
    });
    
    // 监听错误
    xhr.addEventListener('error', (error) => {
        console.error('导入模板失败:', error);
        message.error(t('home.template_import_failed'));
        if (onError) onError(error);
        // 重置状态
        importLoading.value = false;
        showImportProgress.value = false;
        importProgress.value = 0;
    });
    
    // 发送请求
    xhr.open('POST', `http://${apiHost}:${apiPort}/templates/import`);
    xhr.send(formData);
}



// 加载模板商店数据
async function loadStoreTemplates() {
    storeLoading.value = true;
    try {
        const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
        const apiPort = import.meta.env.VITE_API_PORT || '37019';
        const response = await fetch(`http://${apiHost}:${apiPort}/templates/store`);
        const result = await response.json();
        
        if (result.status === 200 && result.data && Array.isArray(result.data.templates)) {
            storeTemplates.value = result.data.templates;
        } else {
            message.error(t('template.store_load_failed'));
        }
    } catch (error) {
        console.error('加载模板商店失败:', error);
        message.error(t('template.store_load_failed'));
    } finally {
        storeLoading.value = false;
    }
}

// 下载并安装模板
async function downloadAndInstallTemplate(template: StoreTemplate) {
    // 重置进度状态
    downloadProgress.value = 0;
    downloadLoading.value = true;
    showDownloadProgress.value = true;
    
    const apiHost = import.meta.env.VITE_API_HOST || 'localhost';
    const apiPort = import.meta.env.VITE_API_PORT || '37019';
    
    try {
        // 测试所有下载链接的速度
        console.log('正在测试下载链接速度...');
        const fastestUrl = await testDownloadSpeed(template.downloadUrls);
        console.log('选择最快的下载链接:', fastestUrl);
        
        // 创建一个唯一的ID用于SSE连接
        const requestId = Math.random().toString(36).substring(2, 10);
        const notificationKey = `download-progress-${requestId}`;
        
        // 检查是否有未完成的下载
        const existingState = downloadStates.value.get(template.id);
        const resumeFrom = existingState ? existingState.downloadedSize : 0;
        
        // 发送POST请求启动下载
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `http://${apiHost}:${apiPort}/templates/install-from-url`);
        xhr.setRequestHeader('Content-Type', 'application/json');
        xhr.onload = () => {
            if (xhr.status === 200) {
                // 处理响应
                console.log('POST请求成功');
            }
        };
        xhr.onerror = () => {
            console.error('POST请求失败');
            message.error(t('template.install_failed'));
            // 重置状态
            downloadLoading.value = false;
            showDownloadProgress.value = false;
            downloadProgress.value = 0;
        };
        xhr.send(JSON.stringify({ url: fastestUrl, requestId, resumeFrom }));
        
        // 使用EventSource接收进度更新
        const eventSource = new EventSource(`http://${apiHost}:${apiPort}/templates/install-from-url?requestId=${requestId}`);
        
        eventSource.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                console.log('收到SSE消息:', data);
                
                switch (data.type) {
                    case 'init':
                        // 初始化信息，包含文件大小
                        console.log('初始化信息:', data);
                        // 存储下载状态
                        downloadStates.value.set(template.id, {
                            url: fastestUrl,
                            downloadedSize: data.resumeFrom || 0,
                            totalSize: data.totalSize || 0
                        });
                        break;
                    case 'progress':
                        // 进度更新
                        downloadProgress.value = data.progress;
                        console.log('进度更新:', data.progress);
                        
                        // 更新下载状态
                        const currentState = downloadStates.value.get(template.id);
                        if (currentState) {
                            downloadStates.value.set(template.id, {
                                ...currentState,
                                downloadedSize: data.downloadedSize || 0,
                                totalSize: data.totalSize || currentState.totalSize
                            });
                        }
                        
                        // 如果用户关闭了进度对话框，在右上角显示进度
                        if (!showDownloadProgress.value) {
                            // 使用相同的key更新通知
                            message.loading({
                                content: `${t('home.template_download_progress')} ${data.progress}%`,
                                duration: 0,
                                key: notificationKey
                            });
                        }
                        break;
                    case 'complete':
                        // 下载完成
                        downloadProgress.value = 100;
                        console.log('下载完成:', data);
                        
                        // 关闭通知
                        message.destroy(notificationKey);
                        
                        // 清理下载状态
                        downloadStates.value.delete(template.id);
                        
                        // 短暂延迟，让用户看到100%的进度
                        setTimeout(async () => {
                            message.success(t('template.install_success'));
                            // 重新加载本地模板列表
                            await loadTemplates();
                            // 重置状态
                            downloadLoading.value = false;
                            showDownloadProgress.value = false;
                            downloadProgress.value = 0;
                            // 关闭EventSource
                            eventSource.close();
                            console.log('重置状态');
                        }, 500);
                        break;
                    case 'error':
                        // 错误信息
                        console.error('下载错误:', data);
                        
                        // 关闭通知
                        message.destroy(notificationKey);
                        
                        const errorMessage = data.message || t('template.install_failed');
                        const errorDetails = data.details ? ` (${data.details})` : '';
                        message.error(errorMessage + errorDetails);
                        // 重置状态
                        downloadLoading.value = false;
                        showDownloadProgress.value = false;
                        downloadProgress.value = 0;
                        // 关闭EventSource
                        eventSource.close();
                        break;
                }
            } catch (error) {
                console.error('解析SSE消息失败:', error);
            }
        };
        
        eventSource.onerror = (error) => {
            console.error('SSE连接错误:', error);
            
            // 关闭通知
            message.destroy(notificationKey);
            
            message.error(t('template.install_failed'));
            // 重置状态
            downloadLoading.value = false;
            showDownloadProgress.value = false;
            downloadProgress.value = 0;
            // 关闭EventSource
            eventSource.close();
        };
    } catch (error) {
        console.error('下载模板失败:', error);
        message.error(t('template.install_failed'));
        // 重置状态
        downloadLoading.value = false;
        showDownloadProgress.value = false;
        downloadProgress.value = 0;
    }
}

onMounted(() => {
    loadTemplates();
});
</script>

<template>
    <div class="tw:h-full tw:w-full tw:overflow-hidden">
        <div class="tw:h-full tw:w-full tw:p-6 tw:overflow-y-auto">
            <div class="tw:max-w-7xl tw:mx-auto">
                <div class="tw:flex tw:justify-between tw:items-center tw:mb-6">
                    <div>
                        <h1 class="tw:text-2xl tw:font-bold tw:text-gray-800">{{ t('template.title') }}</h1>
                        <p class="tw:text-gray-600 tw:mt-1">可选 | 模板是DeEarthX的一种扩展方式，用于快速生成服务端和增加稳定性</p>
                    </div>
                    <div class="tw:flex tw:gap-2">
                        <a-upload
                            name="file"
                            :show-upload-list="false"
                            :custom-request="importTemplate"
                        >
                            <a-button type="default" class="tw:flex tw:items-center tw:gap-2">
                                <UploadOutlined />
                                {{ t('home.template_import_title') }}
                            </a-button>
                        </a-upload>
                        <a-button type="primary" @click="openCreateModal" class="tw:flex tw:items-center tw:gap-2">
                            <PlusOutlined />
                            {{ t('template.create_button') }}
                        </a-button>
                    </div>
                </div>

                <!-- 标签页切换 -->
                <a-tabs v-model:activeKey="activeTab" class="tw:mb-6" @change="(key: string) => {
                    if (key === 'store') {
                        loadStoreTemplates();
                    }
                }">
                    <a-tab-pane key="local" :tab="t('template.local_templates')"></a-tab-pane>
                    <a-tab-pane key="store" :tab="t('template.template_store')"></a-tab-pane>
                </a-tabs>

                <!-- 本地模板 -->
                <a-spin v-if="activeTab === 'local'" :spinning="loading">
                    <div v-if="templates.length === 0 && !loading" class="tw:text-center tw:py-16 tw:text-gray-500">
                        <FolderOutlined style="font-size: 64px; margin-bottom: 16px;" />
                        <p class="tw:text-lg">{{ t('template.empty') }}</p>
                        <p class="tw:text-sm tw:mt-2">{{ t('template.empty_hint') }}</p>
                    </div>

                    <div v-else class="tw:grid tw:grid-cols-1 md:tw:grid-cols-2 lg:tw:grid-cols-3 tw:gap-4">
                        <div 
                            v-for="template in templates" 
                            :key="template.id"
                            class="tw:bg-white tw:rounded-lg tw:shadow-md tw:p-5 tw:h-48 tw:flex tw:flex-col tw:border tw:border-gray-200 tw:transition-all tw:duration-300 hover:tw:shadow-lg hover:tw:border-blue-300"
                        >
                            <div class="tw:flex-1 tw:overflow-hidden">
                                <div class="tw:flex tw:justify-between tw:items-start tw:mb-2">
                                    <h3 class="tw:text-lg tw:font-semibold tw:truncate tw:flex-1 tw:mr-2">{{ template.metadata.name }}</h3>
                                    <a-tag color="blue" size="small">{{ template.metadata.version }}</a-tag>
                                </div>
                                <p class="tw:text-sm tw:text-gray-600 tw:line-clamp-2 tw:mb-3">{{ template.metadata.description }}</p>
                                <div class="tw:flex tw:justify-between tw:text-xs tw:text-gray-500">
                                    <span>{{ t('template.author') }}: {{ template.metadata.author }}</span>
                                    <span>{{ template.metadata.created }}</span>
                                </div>
                            </div>
                            <div class="tw:flex tw:justify-between tw:items-center tw:mt-4 tw:pt-4 tw:border-t tw:border-gray-100">
                                <a-button size="small" @click="openTemplateFolder(template)">
                                    <div class="tw:flex tw:items-center tw:gap-1">
                                        <FolderOutlined />
                                        <span>{{ t('template.open_folder') }}</span>
                                    </div>
                                </a-button>
                                <div class="tw:flex tw:gap-2">
                                    <a-button size="small" @click="exportTemplate(template.id)">
                                        <div class="tw:flex tw:items-center tw:gap-1">
                                            <DownloadOutlined />
                                            <span>{{ t('home.template_export_button') }}</span>
                                        </div>
                                    </a-button>
                                    <a-button size="small" @click="openEditModal(template)">
                                        <div class="tw:flex tw:items-center tw:gap-1">
                                            <EditOutlined />
                                            <span>{{ t('template.edit_button') }}</span>
                                        </div>
                                    </a-button>
                                    <a-button size="small" danger @click="openDeleteModal(template)">
                                        <div class="tw:flex tw:items-center tw:gap-1">
                                            <DeleteOutlined />
                                            <span>{{ t('template.delete_button') }}</span>
                                        </div>
                                    </a-button>
                                </div>
                            </div>
                        </div>
                    </div>
                </a-spin>

                <!-- 模板商店 -->
                <a-spin v-if="activeTab === 'store'" :spinning="storeLoading">
                    <!-- 搜索和筛选 -->
                    <div v-if="storeTemplates.length > 0 && !storeLoading" class="tw:flex tw:flex-wrap tw:justify-between tw:items-center tw:mb-4">
                        <div class="tw:flex tw:gap-2">
                            <a-radio-group v-model:value="selectedTag" class="tw-mr-4">
                                <a-radio-button value="all">全部</a-radio-button>
                                <a-radio-button value="dex">官方</a-radio-button>
                                <a-radio-button value="CE">社区</a-radio-button>
                            </a-radio-group>
                        </div>
                        <a-input-search 
                            v-model:value="searchKeyword"
                            placeholder="搜索模板"
                            style="width: 200px"
                        />
                    </div>
                    
                    <div>
                        <p class="tw:text-xs tw:text-gray-400 tw:mt-1">社区提供的模板官方未经检测，请自行选择使用</p>
                    </div>

                    <div v-if="filteredStoreTemplates.length === 0 && !storeLoading" class="tw:text-center tw:py-16 tw:text-gray-500">
                        <DownloadOutlined style="font-size: 64px; margin-bottom: 16px;" />
                        <p class="tw:text-lg">{{ t('template.store_empty') }}</p>
                        <p class="tw:text-sm tw:mt-2">{{ t('template.store_empty_hint') }}</p>
                    </div>

                    <div v-else class="tw:grid tw:grid-cols-1 md:tw:grid-cols-2 lg:tw:grid-cols-3 tw:gap-4">
                        <div 
                            v-for="template in filteredStoreTemplates" 
                            :key="template.id"
                            class="tw:bg-white tw:rounded-lg tw:shadow-md tw:p-5 tw:h-48 tw:flex tw:flex-col tw:border tw:border-gray-200 tw:transition-all tw:duration-300 hover:tw:shadow-lg hover:tw:border-blue-300"
                        >
                            <div class="tw:flex-1 tw:overflow-hidden">
                                <div class="tw:flex tw:justify-between tw:items-start tw:mb-2">
                                    <h3 class="tw:text-lg tw:font-semibold tw:truncate tw:flex-1 tw:mr-2">{{ template.name }}</h3>
                                    <div class="tw:flex tw:gap-2">
                                        <a-tag color="green" size="small">{{ template.size }}</a-tag>
                                        <a-tag :color="template.tag === 'dex' ? 'blue' : 'orange'" size="small">
                                            {{ template.tag === 'dex' ? '官方' : '社区' }}
                                        </a-tag>
                                    </div>
                                </div>
                                <p class="tw:text-sm tw:text-gray-600 tw:line-clamp-2 tw:mb-3">{{ template.description }}</p>
                                <div class="tw:flex tw:justify-between tw:text-xs tw:text-gray-500">
                                    <span>版本: {{ template.version }}</span>
                                </div>
                            </div>
                            <div class="tw:flex tw:justify-end tw:mt-4 tw:pt-4 tw:border-t tw:border-gray-100">
                                <a-button type="primary" size="small" @click="downloadAndInstallTemplate(template)">
                                    <div class="tw:flex tw:items-center tw:gap-1">
                                        <DownloadOutlined />
                                        <span>{{ t('template.install_button') }}</span>
                                    </div>
                                </a-button>
                            </div>
                        </div>
                    </div>
                </a-spin>
            </div>
        </div>

        <a-modal 
            v-model:open="showCreateModal" 
            :title="t('template.create_title')" 
            @ok="createTemplate"
            :ok-text="t('common.confirm')"
            :cancel-text="t('common.cancel')"
        >
            <a-form layout="vertical">
                <a-form-item :label="t('template.name')" required>
                    <a-input v-model:value="newTemplate.name" :placeholder="t('template.name_placeholder')" />
                </a-form-item>
                <a-form-item :label="t('template.version')">
                    <a-input v-model:value="newTemplate.version" :placeholder="t('template.version_placeholder')" />
                </a-form-item>
                <a-form-item :label="t('template.description')">
                    <a-textarea v-model:value="newTemplate.description" :placeholder="t('template.description_placeholder')" :rows="4" />
                </a-form-item>
                <a-form-item :label="t('template.author')">
                    <a-input v-model:value="newTemplate.author" :placeholder="t('template.author_placeholder')" />
                </a-form-item>
            </a-form>
        </a-modal>

        <a-modal
            v-model:open="showDeleteModal"
            :title="t('template.delete_title')"
            @ok="confirmDelete"
            :ok-text="t('common.confirm')"
            :cancel-text="t('common.cancel')"
            ok-type="danger"
        >
            <div class="tw:flex tw:items-start tw:gap-3">
                <ExclamationCircleOutlined style="font-size: 24px; color: #ff4d4f;" />
                <div>
                    <p class="tw:mb-2">{{ t('template.delete_confirm', { name: deletingTemplate?.metadata.name }) }}</p>
                    <p class="tw:text-sm tw:text-gray-500">{{ t('template.delete_warning') }}</p>
                </div>
            </div>
        </a-modal>

        <a-modal 
            v-model:open="showEditModal" 
            :title="t('template.edit_title')" 
            @ok="updateTemplate"
            :ok-text="t('common.confirm')"
            :cancel-text="t('common.cancel')"
        >
            <a-form layout="vertical">
                <a-form-item :label="t('template.name')" required>
                    <a-input v-model:value="newTemplate.name" :placeholder="t('template.name_placeholder')" />
                </a-form-item>
                <a-form-item :label="t('template.version')">
                    <a-input v-model:value="newTemplate.version" :placeholder="t('template.version_placeholder')" />
                </a-form-item>
                <a-form-item :label="t('template.description')">
                    <a-textarea v-model:value="newTemplate.description" :placeholder="t('template.description_placeholder')" :rows="4" />
                </a-form-item>
                <a-form-item :label="t('template.author')">
                    <a-input v-model:value="newTemplate.author" :placeholder="t('template.author_placeholder')" />
                </a-form-item>
            </a-form>
        </a-modal>

        <!-- 导出进度对话框 -->
        <a-modal
            v-model:open="showExportProgress"
            :title="t('home.template_export_button')"
            :footer="null"
            :closable="false"
        >
            <div class="tw:py-4">
                <p class="tw:text-center tw:mb-4">{{ t('home.template_export_progress') }}</p>
                <a-progress
                    :percent="exportProgress"
                    :status="exportLoading ? 'active' : 'success'"
                    :stroke-width="10"
                />
            </div>
        </a-modal>

        <!-- 导入进度对话框 -->
        <a-modal
            v-model:open="showImportProgress"
            :title="t('home.template_import_title')"
            :footer="null"
            :closable="false"
        >
            <div class="tw:py-4">
                <p class="tw:text-center tw:mb-4">{{ t('home.template_import_progress') }}</p>
                <a-progress
                    :percent="importProgress"
                    :status="importLoading ? 'active' : 'success'"
                    :stroke-width="10"
                />
                <p class="tw:text-center tw:mt-4 tw:text-gray-500">{{ importProgress }}%</p>
            </div>
        </a-modal>

        <!-- 下载进度对话框 -->
        <a-modal
            v-model:open="showDownloadProgress"
            :title="t('template.install_button')"
            :footer="null"
            :closable="false"
        >
            <div class="tw:py-4">
                <p class="tw:text-center tw:mb-4">{{ t('home.template_download_progress') }}</p>
                <a-progress
                    :percent="downloadProgress"
                    :status="downloadLoading ? 'active' : 'success'"
                    :stroke-width="10"
                />
            </div>
        </a-modal>
    </div>
</template>