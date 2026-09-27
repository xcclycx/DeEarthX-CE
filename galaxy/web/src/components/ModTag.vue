<template>
  <a-tag :color="ok ? 'success' : 'error'" style="margin: 0">
    {{ ok ? '✓ 合规' : '✗ 不合规' }}
  </a-tag>
</template>

<script setup lang="ts">
defineProps<{
  ok: boolean
}>()
</script>
