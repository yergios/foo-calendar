<template>
  <ErrorLayout
    code="401"
    message="You are not authenticated."
    :action="{ label: 'Think this is a mistake? Contact support.', href: supportUrl }"
  />
</template>

<script setup lang="ts">
import ErrorLayout from "@/components/ErrorLayout.vue";
import { runtimeConfig } from "@/lib/runtimeConfig";

const supportUrl = runtimeConfig().supportUrl;
</script>
