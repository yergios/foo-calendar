<template>
  <!-- eslint-disable-next-line vue/no-v-html -->
  <div class="prose prose-sm" v-html="rendered" />
</template>

<script setup lang="ts">
import { marked } from "marked";
import { computed } from "vue";

const props = defineProps<{ content: string }>();

const rendered = computed(() => marked.parse(props.content) as string);
</script>

<style scoped>
.prose {
  color: var(--color-ink);
  line-height: 1.7;
}
.prose :deep(h1) {
  font-family: var(--font-display);
  font-weight: var(--font-weight-bold);
  font-size: var(--text-xl);
  margin: 1.5rem 0 0.75rem;
  color: var(--color-ink);
}
.prose :deep(h2) {
  font-family: var(--font-body);
  font-weight: var(--font-weight-semibold);
  font-size: var(--text-base);
  margin: 1.25rem 0 0.5rem;
  color: var(--color-ink);
}
.prose :deep(h3) {
  font-family: var(--font-body);
  font-weight: var(--font-weight-semibold);
  font-size: var(--text-sm);
  margin: 1rem 0 0.5rem;
  color: var(--color-primary);
}
.prose :deep(p) {
  margin: 0.5rem 0;
  font-size: var(--text-sm);
}
.prose :deep(ul) {
  margin: 0.5rem 0 0.5rem 1.25rem;
  font-size: var(--text-sm);
  list-style: disc;
}
.prose :deep(ol) {
  margin: 0.5rem 0 0.5rem 1.25rem;
  font-size: var(--text-sm);
  list-style: decimal;
}
.prose :deep(li) {
  margin: 0.25rem 0;
}
.prose :deep(strong) {
  font-weight: var(--font-weight-semibold);
  color: var(--color-ink);
}
.prose :deep(code) {
  font-family: var(--font-mono, monospace);
  font-size: var(--text-xs);
  background: var(--color-surface);
  padding: 0.1rem 0.3rem;
  border-radius: var(--radius-4);
}
.prose :deep(hr) {
  border: none;
  border-top: 1px solid var(--color-border);
  margin: 1.25rem 0;
}
.prose :deep(a) {
  color: var(--color-primary-text);
  text-underline-offset: 2px;
}

.prose :deep(a:hover) {
  text-decoration: underline;
}
</style>
