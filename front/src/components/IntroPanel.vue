<script lang="ts" setup>
import { RocketOutlined } from '@ant-design/icons-vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();

defineProps<{
  tips: string[];
}>();
</script>

<template>
  <div class="tw:bg-white tw:rounded-xl tw:shadow-sm tw:border tw:border-gray-100 tw:p-6 tw:mb-4">
    <div class="tw:flex tw:items-center tw:gap-2 tw:mb-4">
      <RocketOutlined class="tw:text-blue-500 tw:text-lg" />
      <h2 class="tw:text-lg tw:font-semibold tw:text-gray-800">{{ t('main.welcome') }}</h2>
    </div>
    <div class="tw:space-y-2">
      <div
        v-for="(tip, index) in tips"
        :key="index"
        class="tw:flex tw:items-start tw:gap-2 tw:text-sm tw:text-gray-600"
      >
        <span class="tw:text-blue-400 tw:font-bold tw:shrink-0">#</span>
        <span>{{ tip }}</span>
      </div>
    </div>
  </div>
</template>