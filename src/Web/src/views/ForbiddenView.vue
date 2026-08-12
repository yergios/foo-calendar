<template>
  <ErrorLayout
    code="403"
    message="You don't have access to this application."
    :action="{ label: 'Think this is a mistake? Contact support.', href: supportUrl }"
  />
</template>

<script setup lang="ts">
import ErrorLayout from "@/components/ErrorLayout.vue";
import { runtimeConfig } from "@/lib/runtimeConfig";

const supportUrl = runtimeConfig().supportUrl;
</script>
