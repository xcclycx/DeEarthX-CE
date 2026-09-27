<script lang="ts" setup>
import { ref } from 'vue';

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
  'submit': [password: string];
}>();

const importPassword = ref('');
const importProcessing = ref(false);

function handleSubmit() {
  if (!importPassword.value.trim()) {
    return;
  }
  importProcessing.value = true;
  emit('submit', importPassword.value);
  importProcessing.value = false;
}
</script>

<template>
  <a-modal
    :open="visible"
    title="输入解密密码"
    :closable="true"
    width="400px"
    :footer="null"
    @cancel="emit('update:visible', false)"
  >
    <div class="tw:py-2">
      <p class="tw:text-sm tw:text-gray-600 tw:mb-4">此插件已私有加密，请输入解密密码</p>
      <a-input-password
        v-model:value="importPassword"
        placeholder="请输入解密密码"
        class="tw:mb-3"
      />
      <a-button type="primary" :loading="importProcessing" block @click="handleSubmit">
        解密并导入
      </a-button>
    </div>
  </a-modal>
</template>