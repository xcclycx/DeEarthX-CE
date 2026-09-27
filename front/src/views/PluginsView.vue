<script lang="ts" setup>
import { ref, reactive, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { message, Modal } from 'ant-design-vue';
import {
  ApiOutlined,
  PlusOutlined,
  ExportOutlined,
  DeleteOutlined,
  UploadOutlined,
  ToolOutlined,
  FolderOpenOutlined,
  LockOutlined,
  ReloadOutlined,
  WarningOutlined
} from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';
import {
  pluginApi,
  describeError,
  generatePluginId,
  type PluginDiagnostics,
  type PluginInfo
} from '../utils/pluginApi';

const { t } = useI18n();
const router = useRouter();

const plugins = ref<PluginInfo[]>([]);
const loading = ref(false);
const reloading = ref(false);
const diagnostics = ref<PluginDiagnostics | null>(null);
const diagnosticsVisible = ref(false);

const installModalVisible = ref(false);
const createModalVisible = ref(false);
const creating = ref(false);

const encryptExportModalVisible = ref(false);
const encryptExportPlugin = ref<PluginInfo | null>(null);
const encryptExportMode = ref<'public' | 'private'>('public');
const encryptExportPassword = ref('');
const encryptExportRememberPassword = ref(false);
const encryptExportProcessing = ref(false);

const importPasswordModalVisible = ref(false);
const importPassword = ref('');
const importRememberPassword = ref(false);
const importEncryptedFile = ref<File | null>(null);
const importProcessing = ref(false);

const createForm = reactive({
  name: '',
  author: '',
  url: '',
  withTutorial: true
});

const generatedId = ref(generatePluginId());

/** 依赖缺失 / 加载失败在前端直接可见，避免“装了但没生效”却无从查起。 */
const failedPlugins = computed(() => diagnostics.value?.failures ?? []);

const pluginCapabilities = (plugin: PluginInfo): string[] => {
  const capabilities = plugin.manifest.capabilities;
  const tags: string[] = [];

  if (capabilities?.hooks?.length) tags.push(`${capabilities.hooks.length} 个钩子`);
  if (capabilities?.filterStrategies?.length) tags.push(`${capabilities.filterStrategies.length} 条过滤策略`);
  if (capabilities?.pages) tags.push('页面');
  if (capabilities?.sidebar) tags.push('侧边栏');
  if (capabilities?.injectScripts?.length || capabilities?.injectStyles?.length) tags.push('注入');

  return tags;
};

function resetCreateForm() {
  createForm.name = '';
  createForm.author = '';
  createForm.url = '';
  createForm.withTutorial = true;
  generatedId.value = generatePluginId();
}

function openCreateModal() {
  resetCreateForm();
  createModalVisible.value = true;
}

function closeCreateModal() {
  createModalVisible.value = false;
}

async function loadPlugins() {
  loading.value = true;
  try {
    plugins.value = await pluginApi.list();
  } catch (error) {
    message.error(describeError(error, t('common.error')));
  } finally {
    loading.value = false;
  }
}

async function loadDiagnostics() {
  try {
    diagnostics.value = await pluginApi.diagnostics();
  } catch {
    // 诊断信息属于可选增强，失败时静默处理，不打扰主流程。
    diagnostics.value = null;
  }
}

async function refreshAll() {
  await loadPlugins();
  await loadDiagnostics();
}

async function handleCreate() {
  if (!createForm.name.trim()) {
    message.warning(t('plugin.create_name_required') || '请输入插件名称');
    return;
  }
  if (!createForm.author.trim()) {
    message.warning(t('plugin.create_author_required') || '请输入作者');
    return;
  }

  creating.value = true;
  try {
    await pluginApi.create({
      name: createForm.name.trim(),
      author: createForm.author.trim(),
      url: createForm.url.trim()
    });
    message.success(t('plugin.create_success') || '插件创建成功');
    closeCreateModal();
    await refreshAll();
  } catch (error) {
    message.error(describeError(error, t('plugin.create_failed') || '创建插件失败'));
  } finally {
    creating.value = false;
  }
}

async function openPluginFolder() {
  try {
    await pluginApi.openFolder();
    message.success(t('plugin.open_folder_success') || '插件文件夹已打开');
  } catch (error) {
    message.error(describeError(error, t('plugin.open_folder_failed') || '打开文件夹失败'));
  }
}

/** 全量热重载：手工改完插件目录后不需要重启应用。 */
async function reloadAllPlugins() {
  reloading.value = true;
  try {
    const report = await pluginApi.reloadAll();

    if (report.failed > 0) {
      message.warning(`已重载 ${report.loaded} 个插件，${report.failed} 个未能加载（${report.elapsedMs}ms）`);
      diagnosticsVisible.value = true;
    } else {
      message.success(`已重载 ${report.loaded} 个插件（${report.elapsedMs}ms）`);
    }

    await refreshAll();
  } catch (error) {
    message.error(describeError(error, '重载插件失败'));
  } finally {
    reloading.value = false;
  }
}

async function togglePlugin(plugin: PluginInfo, enable: boolean) {
  try {
    if (enable) {
      await pluginApi.enable(plugin.manifest.id);
    } else {
      await pluginApi.disable(plugin.manifest.id);
    }

    message.success(enable ? t('plugin.enable_success') : t('plugin.disable_success'));
    await refreshAll();
  } catch (error) {
    message.error(describeError(error, enable ? t('plugin.enable_failed') : t('plugin.disable_failed')));
  }
}

function deletePlugin(plugin: PluginInfo) {
  Modal.confirm({
    title: t('plugin.delete_title'),
    content: t('plugin.delete_confirm', { name: plugin.manifest.name }),
    okText: t('common.delete'),
    cancelText: t('common.cancel'),
    okButtonProps: { danger: true },
    onOk: async () => {
      try {
        await pluginApi.uninstall(plugin.manifest.id, true);
        message.success(t('plugin.delete_success'));
        await refreshAll();
      } catch (error) {
        message.error(describeError(error, t('plugin.delete_failed')));
      }
    }
  });
}

function exportPlugin(plugin: PluginInfo) {
  try {
    pluginApi.exportZip(plugin.manifest.id);
    message.success(t('plugin.export_success'));
  } catch (error) {
    message.error(describeError(error, t('plugin.export_failed')));
  }
}

function openEncryptExportModal(plugin: PluginInfo) {
  encryptExportPlugin.value = plugin;
  encryptExportMode.value = 'public';
  encryptExportPassword.value = '';
  encryptExportRememberPassword.value = false;
  encryptExportModalVisible.value = true;
}

function closeEncryptExportModal() {
  encryptExportModalVisible.value = false;
  encryptExportPlugin.value = null;
}

async function handleEncryptExport() {
  const target = encryptExportPlugin.value;
  if (!target) return;

  if (encryptExportMode.value === 'private' && !encryptExportPassword.value.trim()) {
    message.warning('请输入加密密码');
    return;
  }
  if (encryptExportMode.value === 'private' && !/^[a-zA-Z0-9]+$/.test(encryptExportPassword.value)) {
    message.warning('密码仅能包含大小写字母和数字');
    return;
  }

  encryptExportProcessing.value = true;
  try {
    await pluginApi.exportEncrypted(
      target.manifest.id,
      encryptExportMode.value,
      encryptExportMode.value === 'private' ? encryptExportPassword.value : '',
      `${target.manifest.name}-encrypted.dxp`
    );
    message.success('加密导出成功');
    closeEncryptExportModal();
  } catch (error) {
    message.error(describeError(error, '加密导出失败'));
  } finally {
    encryptExportProcessing.value = false;
  }
}

function navigateToDetail(plugin: PluginInfo) {
  router.push(`/plugin/${plugin.manifest.id}`);
}

function openInstallModal() {
  installModalVisible.value = true;
}

function closeInstallModal() {
  installModalVisible.value = false;
}

async function handleInstallFile(file: File) {
  try {
    const result = await pluginApi.install(file);

    if (result.requirePassword) {
      importEncryptedFile.value = file;
      importPassword.value = '';
      importRememberPassword.value = false;
      importPasswordModalVisible.value = true;
      return;
    }

    message.success(t('plugin.install_success'));
    closeInstallModal();
    await refreshAll();
  } catch (error) {
    message.error(describeError(error, t('plugin.install_failed')));
  }
}

async function handleImportWithPassword() {
  if (!importPassword.value.trim()) {
    message.warning('请输入解密密码');
    return;
  }

  importProcessing.value = true;
  try {
    await pluginApi.importEncrypted(importEncryptedFile.value!, importPassword.value);
    message.success('加密插件导入成功');
    importPasswordModalVisible.value = false;
    closeInstallModal();
    await refreshAll();
  } catch (error) {
    message.error(describeError(error, '导入加密插件失败'));
  } finally {
    importProcessing.value = false;
  }
}

function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;

  const file = input.files[0];
  if (!file.name.endsWith('.zip') && !file.name.endsWith('.dxp')) {
    message.warning('请选择 .zip 或 .dxp 格式的插件包');
    return;
  }

  handleInstallFile(file);
}

onMounted(() => {
  refreshAll();
});
</script>

<template>
  <div class="tw:h-full tw:w-full tw:flex tw:flex-col tw:p-6 tw:overflow-hidden">
    <div class="tw:flex tw:justify-between tw:items-center tw:mb-4">
      <div>
        <h1 class="tw:text-2xl tw:font-bold tw:text-gray-800">{{ t('plugin.title') }}</h1>
        <p class="tw:text-sm tw:text-gray-500 tw:mt-1">{{ t('plugin.description') }}</p>
      </div>
      <div class="tw:flex tw:gap-2">
        <a-button v-if="diagnostics" class="tw:inline-flex tw:items-center" @click="diagnosticsVisible = true">
          <template #icon><WarningOutlined /></template>
          <span>诊断</span>
          <a-badge v-if="failedPlugins.length" :count="failedPlugins.length" :offset="[6, -6]" class="tw:ml-1" />
        </a-button>
        <a-button class="tw:inline-flex tw:items-center" :loading="reloading" @click="reloadAllPlugins">
          <template #icon><ReloadOutlined /></template>
          <span>重新加载</span>
        </a-button>
        <a-button class="tw:inline-flex tw:items-center" @click="openPluginFolder">
          <template #icon><FolderOpenOutlined /></template>
          <span>{{ t('plugin.open_folder_button') || '打开插件文件夹' }}</span>
        </a-button>
        <a-button class="tw:inline-flex tw:items-center" @click="openCreateModal">
          <template #icon><ToolOutlined /></template>
          <span>{{ t('plugin.create_button') || '创建插件' }}</span>
        </a-button>
        <a-button class="tw:inline-flex tw:items-center" type="primary" @click="openInstallModal">
          <template #icon><PlusOutlined /></template>
          <span>{{ t('plugin.install_button') }}</span>
        </a-button>
      </div>
    </div>

    <a-alert
      v-if="failedPlugins.length"
      type="warning"
      show-icon
      class="tw:mb-4"
      :message="`有 ${failedPlugins.length} 个插件未能加载`"
    >
      <template #description>
        <ul class="tw:m-0 tw:pl-4 tw:text-xs">
          <li v-for="failure in failedPlugins" :key="failure.id">
            <span class="tw:font-medium">{{ failure.id }}</span>：{{ failure.error }}
          </li>
        </ul>
      </template>
    </a-alert>

    <a-spin :spinning="loading" class="tw:flex-1">
      <div v-if="plugins.length === 0 && !loading" class="tw:h-full tw:flex tw:flex-col tw:items-center tw:justify-center tw:text-gray-400">
        <ApiOutlined class="tw:text-6xl tw:mb-4" />
        <p class="tw:text-lg">{{ t('plugin.no_plugins') }}</p>
        <p class="tw:text-sm">{{ t('plugin.no_plugins_hint') }}</p>
      </div>

      <div v-else class="tw:grid tw:grid-cols-1 tw:md:grid-cols-2 tw:lg:grid-cols-3 tw:gap-4">
        <div
          v-for="plugin in plugins"
          :key="plugin.manifest.id"
          class="tw:bg-white tw:rounded-xl tw:shadow-sm tw:border tw:border-gray-100 tw:p-5 tw:cursor-pointer tw:transition-all tw:duration-300 hover:tw:shadow-md hover:tw:border-gray-200 tw:flex tw:flex-col"
          @click="navigateToDetail(plugin)"
        >
          <div class="tw:flex tw:items-start tw:justify-between tw:mb-3">
            <div class="tw:flex tw:items-center tw:gap-3 tw:flex-1 tw:min-w-0">
              <div v-if="plugin.manifest.icon" class="tw:w-9 tw:h-9 tw:rounded-lg tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-lg tw:shrink-0">
                {{ plugin.manifest.icon }}
              </div>
              <div v-else class="tw:w-9 tw:h-9 tw:rounded-lg tw:bg-gradient-to-br tw:from-blue-50 tw:to-indigo-100 tw:flex tw:items-center tw:justify-center tw:text-base tw:shrink-0">
                <ToolOutlined class="tw:text-blue-500" />
              </div>
              <div class="tw:min-w-0">
                <h3 class="tw:text-base tw:font-semibold tw:text-gray-800 tw:truncate">{{ plugin.manifest.name }}</h3>
                <p class="tw:text-xs tw:text-gray-500 tw:mt-0.5">
                  {{ plugin.manifest.author }} · v{{ plugin.manifest.version }}
                </p>
              </div>
            </div>
            <a-tag :color="plugin.enabled ? 'green' : 'default'" class="tw:shrink-0 tw:ml-2">
              {{ plugin.enabled ? t('plugin.plugin_status_enabled') : t('plugin.plugin_status_disabled') }}
            </a-tag>
          </div>

          <p v-if="plugin.manifest.description" class="tw:text-sm tw:text-gray-600 tw:line-clamp-2 tw:mb-3">
            {{ plugin.manifest.description }}
          </p>

          <div v-if="pluginCapabilities(plugin).length" class="tw:flex tw:flex-wrap tw:gap-1 tw:mb-3">
            <a-tag v-for="capability in pluginCapabilities(plugin)" :key="capability" color="blue" class="tw:text-xs">
              {{ capability }}
            </a-tag>
          </div>

          <div class="tw:flex-1" />

          <div class="tw:flex tw:items-center tw:gap-2 tw:pt-3 tw:border-t tw:border-gray-50" @click.stop>
            <a-switch
              :checked="plugin.enabled"
              size="small"
              @change="(checked: boolean) => togglePlugin(plugin, checked)"
            />
            <span class="tw:text-xs tw:text-gray-500">
              {{ plugin.enabled ? t('common.start') : t('common.close') }}
            </span>
            <div class="tw:flex-1" />
            <a-button size="small" type="text" class="tw:inline-flex tw:items-center tw:justify-center" @click="exportPlugin(plugin)" :title="t('plugin.export_button')">
              <template #icon><ExportOutlined /></template>
            </a-button>
            <a-button size="small" type="text" class="tw:inline-flex tw:items-center tw:justify-center" @click.stop="openEncryptExportModal(plugin)" title="加密导出">
              <template #icon><LockOutlined /></template>
            </a-button>
            <a-button size="small" type="text" danger class="tw:inline-flex tw:items-center tw:justify-center" @click="deletePlugin(plugin)" :title="t('plugin.delete_button')">
              <template #icon><DeleteOutlined /></template>
            </a-button>
          </div>
        </div>
      </div>
    </a-spin>

    <!-- 诊断抽屉：扫描耗时、钩子声明、扩展点、加载失败原因 -->
    <a-drawer v-model:open="diagnosticsVisible" title="插件系统诊断" placement="right" :width="520">
      <template v-if="diagnostics">
        <a-descriptions :column="2" size="small" bordered class="tw:mb-5">
          <a-descriptions-item label="已加载">{{ diagnostics.total }}</a-descriptions-item>
          <a-descriptions-item label="已启用">{{ diagnostics.enabled }}</a-descriptions-item>
          <a-descriptions-item label="扫描耗时">{{ diagnostics.loadedInMilliseconds }} ms</a-descriptions-item>
          <a-descriptions-item label="热重载">{{ diagnostics.hotReload ? '已启用' : '不可用' }}</a-descriptions-item>
        </a-descriptions>

        <h3 class="tw:text-sm tw:font-semibold tw:mb-2">已声明的钩子</h3>
        <div class="tw:flex tw:flex-wrap tw:gap-1 tw:mb-5">
          <a-tag v-for="hook in diagnostics.declaredHooks" :key="hook" color="geekblue">{{ hook }}</a-tag>
          <span v-if="!diagnostics.declaredHooks.length" class="tw:text-xs tw:text-gray-400">暂无插件声明钩子</span>
        </div>

        <h3 class="tw:text-sm tw:font-semibold tw:mb-2">扩展点</h3>
        <div class="tw:mb-5">
          <div v-for="point in diagnostics.extensionPoints" :key="`${point.type}-${point.kind}`" class="tw:text-xs tw:text-gray-600">
            {{ point.kind }} · {{ point.count }} 项（{{ point.owners.join(', ') }}）
          </div>
          <span v-if="!diagnostics.extensionPoints.length" class="tw:text-xs tw:text-gray-400">暂无注册的扩展贡献</span>
        </div>

        <h3 class="tw:text-sm tw:font-semibold tw:mb-2">未能加载</h3>
        <a-list size="small" :data-source="diagnostics.failures">
          <template #renderItem="{ item }">
            <a-list-item>
              <a-list-item-meta :title="item.id" :description="item.error" />
              <a-tag color="orange">{{ item.state }}</a-tag>
            </a-list-item>
          </template>
        </a-list>
        <span v-if="!diagnostics.failures.length" class="tw:text-xs tw:text-gray-400">全部插件加载正常</span>
      </template>
    </a-drawer>

    <a-modal
      v-model:open="installModalVisible"
      :title="t('plugin.install_title')"
      :footer="null"
      :closable="true"
      width="480px"
    >
      <div class="tw:p-4">
        <a-alert
          message="安全提醒"
          description="我们不保证你导入的插件绝对安全，请仔细辨别来源后导入。"
          type="warning"
          show-icon
          class="tw:mb-4"
        />
        <div
          class="tw:border-2 tw:border-dashed tw:border-gray-300 tw:rounded-lg tw:p-8 tw:text-center tw:cursor-pointer hover:tw:border-blue-400 hover:tw:bg-blue-50 tw:transition-all"
          @click="($event) => ($event.target as HTMLElement)?.querySelector('input')?.click()"
          @dragover.prevent
          @drop.prevent="(e) => {
            const files = e.dataTransfer?.files;
            if (files && files.length > 0) handleInstallFile(files[0]);
          }"
        >
          <UploadOutlined class="tw:text-4xl tw:text-gray-400 tw:mb-3" />
          <p class="tw:text-gray-600">{{ t('plugin.install_hint') }}</p>
          <p class="tw:text-xs tw:text-gray-400 tw:mt-2">支持 .zip 和 .dxp（加密插件）格式</p>
          <input type="file" accept=".zip,.dxp" class="tw:hidden" @change="handleFileSelect" />
        </div>
      </div>
    </a-modal>

    <a-modal
      v-model:open="createModalVisible"
      :title="t('plugin.create_title') || '创建插件'"
      :closable="true"
      width="520px"
      @cancel="closeCreateModal"
    >
      <a-form :label-col="{ span: 5 }" :wrapper-col="{ span: 19 }">
        <a-form-item :label="t('plugin.create_id_label') || '插件 ID'" required>
          <a-input :value="generatedId" disabled class="tw:bg-gray-50">
            <template #suffix>
              <span class="tw:text-xs tw:text-gray-400">自动生成</span>
            </template>
          </a-input>
          <p class="tw:text-xs tw:text-gray-400 tw:mt-1">{{ t('plugin.create_id_hint') || '由系统自动生成 10 位数字 ID，不可修改' }}</p>
        </a-form-item>

        <a-form-item :label="t('plugin.create_name_label') || '插件名称'" required>
          <a-input v-model:value="createForm.name" :placeholder="t('plugin.create_name_placeholder') || '请输入插件名称'" />
        </a-form-item>

        <a-form-item :label="t('plugin.create_author_label') || '作者'" required>
          <a-input v-model:value="createForm.author" :placeholder="t('plugin.create_author_placeholder') || '请输入作者名称'" />
        </a-form-item>

        <a-form-item :label="t('plugin.create_url_label') || '链接'">
          <a-input v-model:value="createForm.url" :placeholder="t('plugin.create_url_placeholder') || '可选，如 https://github.com/...'" />
        </a-form-item>

        <a-form-item :wrapper-col="{ offset: 5, span: 19 }">
          <a-checkbox v-model:checked="createForm.withTutorial">
            {{ t('plugin.create_with_tutorial') || '同时生成示例页面与钩子脚本（推荐）' }}
          </a-checkbox>
        </a-form-item>
      </a-form>

      <template #footer>
        <a-button @click="closeCreateModal">{{ t('common.cancel') }}</a-button>
        <a-button type="primary" :loading="creating" @click="handleCreate">
          {{ t('plugin.create_confirm') || '创建' }}
        </a-button>
      </template>
    </a-modal>

    <a-modal
      v-model:open="encryptExportModalVisible"
      title="加密导出插件"
      :closable="true"
      width="480px"
      @cancel="closeEncryptExportModal"
    >
      <div class="tw:py-2">
        <p class="tw:text-sm tw:text-gray-600 tw:mb-4">
          选择加密方式导出插件「{{ encryptExportPlugin?.manifest.name }}」
        </p>

        <a-radio-group v-model:value="encryptExportMode" class="tw:w-full">
          <div class="tw:border tw:rounded-lg tw:p-4 tw:mb-3 tw:cursor-pointer" :class="encryptExportMode === 'public' ? 'tw:border-blue-400 tw:bg-blue-50' : 'tw:border-gray-200'">
            <a-radio value="public" class="tw:flex tw:items-start">
              <div>
                <div class="tw:font-medium tw:text-gray-800">公开加密</div>
                <div class="tw:text-xs tw:text-gray-500 tw:mt-1">使用固定密钥（DeEarthX-CE），插件系统可自动识别解密</div>
              </div>
            </a-radio>
          </div>
          <div class="tw:border tw:rounded-lg tw:p-4 tw:mb-3 tw:cursor-pointer" :class="encryptExportMode === 'private' ? 'tw:border-blue-400 tw:bg-blue-50' : 'tw:border-gray-200'">
            <a-radio value="private" class="tw:flex tw:items-start">
              <div>
                <div class="tw:font-medium tw:text-gray-800">私有加密</div>
                <div class="tw:text-xs tw:text-gray-500 tw:mt-1">使用自定义密钥，导入时需输入密码</div>
              </div>
            </a-radio>
          </div>
        </a-radio-group>

        <a-input-password
          v-if="encryptExportMode === 'private'"
          v-model:value="encryptExportPassword"
          placeholder="请输入加密密码（仅包含大小写字母和数字）"
          class="tw:mt-2"
        />

        <p></p>

        <a-checkbox
          v-if="encryptExportMode === 'private'"
          v-model:checked="encryptExportRememberPassword"
          class="tw:mt-2"
        >
          记住密码
        </a-checkbox>
      </div>

      <template #footer>
        <a-button @click="closeEncryptExportModal">取消</a-button>
        <a-button type="primary" :loading="encryptExportProcessing" @click="handleEncryptExport">
          加密导出
        </a-button>
      </template>
    </a-modal>

    <a-modal
      v-model:open="importPasswordModalVisible"
      title="输入解密密码"
      :closable="true"
      width="400px"
      :footer="null"
    >
      <div class="tw:py-2">
        <p class="tw:text-sm tw:text-gray-600 tw:mb-4">此插件已私有加密，请输入解密密码</p>
        <a-input-password v-model:value="importPassword" placeholder="请输入解密密码" class="tw:mb-3" />
        <a-checkbox v-model:checked="importRememberPassword" class="tw:mb-4">记住密码</a-checkbox>

        <p></p>

        <a-button type="primary" :loading="importProcessing" block @click="handleImportWithPassword">
          解密并导入
        </a-button>
      </div>
    </a-modal>
  </div>
</template>
