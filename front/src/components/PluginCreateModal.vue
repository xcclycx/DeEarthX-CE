<script lang="ts" setup>
import { reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
  'create': [name: string, author: string, url: string];
}>();

const creating = ref(false);

function generateRandomId(): string {
  let id = '';
  for (let i = 0; i < 10; i++) {
    id += Math.floor(Math.random() * 10).toString();
  }
  return id;
}

const generatedId = ref(generateRandomId());

const createForm = reactive({
  name: '',
  author: '',
  url: '',
});

function closeCreateModal() {
  emit('update:visible', false);
}

function resetCreateForm() {
  createForm.name = '';
  createForm.author = '';
  createForm.url = '';
  generatedId.value = generateRandomId();
}

async function handleCreate() {
  if (!createForm.name.trim()) {
    return;
  }
  if (!createForm.author.trim()) {
    return;
  }
  creating.value = true;
  try {
    emit('create', createForm.name.trim(), createForm.author.trim(), createForm.url.trim());
    closeCreateModal();
    resetCreateForm();
  } finally {
    creating.value = false;
  }
}
</script>

<template>
  <a-modal
    :open="visible"
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
    </a-form>

    <template #footer>
      <a-button @click="closeCreateModal">{{ t('common.cancel') }}</a-button>
      <a-button type="primary" :loading="creating" @click="handleCreate">
        {{ t('plugin.create_confirm') || '创建' }}
      </a-button>
    </template>
  </a-modal>
</template>