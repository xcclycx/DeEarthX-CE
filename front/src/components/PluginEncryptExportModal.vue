<script lang="ts" setup>
import { ref } from 'vue';

defineProps<{
  visible: boolean;
  pluginName: string;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
  'export': [mode: string, password: string];
}>();

const encryptExportMode = ref<'public' | 'private'>('public');
const encryptExportPassword = ref('');

function closeModal() {
  emit('update:visible', false);
}

function handleExport() {
  if (encryptExportMode.value === 'private' && !encryptExportPassword.value.trim()) {
    return;
  }
  emit('export', encryptExportMode.value, encryptExportPassword.value);
}
</script>

<template>
  <a-modal
    :open="visible"
    title="加密导出插件"
    :closable="true"
    width="480px"
    @cancel="closeModal"
  >
    <div class="tw:py-2">
      <p class="tw:text-sm tw:text-gray-600 tw:mb-4">
        选择加密方式导出插件「{{ pluginName }}」
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
    </div>

    <template #footer>
      <a-button @click="closeModal">取消</a-button>
      <a-button type="primary" @click="handleExport">加密导出</a-button>
    </template>
  </a-modal>
</template>