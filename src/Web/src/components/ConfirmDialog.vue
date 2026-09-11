<template>
  <AppModal :title="title" size="md" role="alertdialog" @close="$emit('cancel')">
    <p id="confirm-dialog-message" class="text-sm text-ink">{{ message }}</p>

    <template #footer>
      <div class="flex items-center gap-3">
        <!-- Cancel first, and focused on open. Both buttons are outlined and so carry the
             same visual weight; what keeps a destructive action from happening by reflex is
             where the focus starts and the fact that Escape cancels, not colour. -->
        <button
          ref="cancelRef"
          type="button"
          class="flex-1 border border-accent text-accent rounded-8 py-2.5 text-sm font-semibold hover:bg-accent/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
          :disabled="busy"
          @click="$emit('cancel')"
        >
          Cancel
        </button>
        <button
          type="button"
          class="flex-1 rounded-8 border py-2.5 text-sm font-semibold transition-colors disabled:opacity-40 disabled:hover:bg-background"
          :class="
            destructive
              ? 'text-error border-error hover:bg-error/10'
              : 'text-accent border-accent hover:bg-accent/10'
          "
          :disabled="busy"
          @click="$emit('confirm')"
        >
          {{ busy ? busyLabel : confirmLabel }}
        </button>
      </div>
    </template>
  </AppModal>
</template>

<script setup lang="ts">
import { onMounted, useTemplateRef } from "vue";

import AppModal from "@/components/AppModal.vue";

/**
 * The app's confirmation step for anything that can't be undone. Wraps AppModal rather than
 * reimplementing a dialog, so the backdrop, the escape key and the close button all behave
 * the way every other modal does.
 */
withDefaults(
  defineProps<{
    title: string;
    message: string;
    confirmLabel?: string;
    busyLabel?: string;
    destructive?: boolean;
    busy?: boolean;
  }>(),
  { confirmLabel: "Confirm", busyLabel: "Working…", destructive: false, busy: false },
);

defineEmits<{ confirm: []; cancel: [] }>();

const cancelRef = useTemplateRef<HTMLButtonElement>("cancelRef");

// The safe option is where the keyboard already is, so Enter on a dialog you didn't read
// backs out rather than going through with it.
onMounted(() => cancelRef.value?.focus());
</script>
