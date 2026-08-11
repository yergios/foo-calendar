<template>
  <ErrorLayout
    code="500"
    message="Something went wrong on our end."
    :action="{ label: 'Raise an issue', href: issueUrl }"
  />
</template>

<script setup lang="ts">
import ErrorLayout from "@/components/ErrorLayout.vue";

const issueUrl = import.meta.env.VITE_ISSUE_URL;
</script>
