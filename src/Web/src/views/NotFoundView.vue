<template>
  <ErrorLayout
    code="404"
    message="The page you're looking for doesn't exist."
    :action="{ label: 'Go back home', href: '/', internal: true }"
  />
</template>

<script setup lang="ts">
import ErrorLayout from "@/components/ErrorLayout.vue";
</script>
