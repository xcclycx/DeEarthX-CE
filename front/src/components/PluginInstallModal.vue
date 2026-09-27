<script lang="ts" setup>
import { UploadOutlined } from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
  'installFile': [file: File];
}>();

function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement;
  if (input.files && input.files.length > 0) {
    emit('installFile', input.files[0]);
  }
}
</script>

<template>
  <a-modal
    :open="visible"
    :title="t('plugin.install_title')"
    :footer="null"
    :closable="true"
    width="480px"
    @cancel="emit('update:visible', false)"
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
          if (files && files.length > 0) emit('installFile', files[0]);
        }"
      >
        <UploadOutlined class="tw:text-4xl tw:text-gray-400 tw:mb-3" />
        <p class="tw:text-gray-600">{{ t('plugin.install_hint') }}</p>
        <p class="tw:text-xs tw:text-gray-400 tw:mt-2">支持 .zip 和 .dxp（加密插件）格式</p>
        <input
          type="file"
          accept=".zip,.dxp"
          class="tw:hidden"
          @change="handleFileSelect"
        />
      </div>
    </div>
  </a-modal>
</template>