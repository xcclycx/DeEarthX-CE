<template>
  <a-tag :color="colorMap[permission] || 'default'" style="margin: 0">
    {{ labelMap[permission] || permission }}
  </a-tag>
</template>

<script setup lang="ts">
defineProps<{
  permission: string
}>()

const colorMap: Record<string, string> = {
  'mod.submit': 'blue',
  'mod.manage': 'orange',
  'mod.query': 'cyan',
  'user.manage': 'red',
  'system.settings': 'purple',
  'apikey.manage': 'green',
}

const labelMap: Record<string, string> = {
  'mod.submit': '提交模组',
  'mod.manage': '管理模组',
  'mod.query': '查询模组',
  'user.manage': '管理用户',
  'system.settings': '系统设置',
  'apikey.manage': '管理密钥',
}
</script>
