<template>
  <div class="flex flex-col gap-5">
    <div class="flex flex-col gap-1.5">
      <label for="displayName" class="text-xs font-semibold text-secondary"
        >Display name<span class="text-error ml-0.5" title="Required">*</span></label
      >
      <input
        id="displayName"
        v-model="displayName"
        type="text"
        maxlength="100"
        placeholder="e.g. David R."
        class="border border-border rounded-8 px-3 py-2"
      />
    </div>

    <div class="flex flex-col gap-1.5">
      <label for="homeCity" class="text-xs font-semibold text-secondary">Home city</label>
      <AppSkeleton v-if="configLoading" class="h-10 w-full" />
      <select
        v-else
        id="homeCity"
        v-model="homeCity"
        class="border border-border rounded-8 px-3 py-2 bg-background"
      >
        <option :value="null">Not set</option>
        <optgroup v-for="country in regionCountries" :key="country.code" :label="country.name">
          <option v-for="opt in country.regions" :key="opt.code" :value="opt.code">
            {{ opt.label }}
          </option>
        </optgroup>
      </select>
    </div>

    <div class="flex flex-col gap-1.5">
      <label class="text-xs font-semibold text-secondary"
        >Color<span class="text-error ml-0.5" title="Required">*</span></label
      >
      <ColorPicker
        v-model="color"
        :palette="palette"
        :colors-in-use="colorsInUse"
        :exclude="initialColor"
        :loading="configLoading"
      />
    </div>

    <p v-if="error" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2">{{ error }}</p>

    <button
      type="button"
      class="bg-primary text-white border border-transparent rounded-8 py-2.5 transition-all duration-150 ease-out hover:bg-primary-hover hover:shadow-card active:scale-98 disabled:opacity-40 disabled:hover:bg-primary disabled:hover:shadow-none"
      :title="canSubmit ? undefined : 'Set display name and color first.'"
      :disabled="!canSubmit || submitting"
      @click="submit"
    >
      {{ submitting ? "Saving…" : submitLabel }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";

import { getColorsInUse } from "@/api/people";
import { useAuthStore } from "@/auth";
import AppSkeleton from "@/components/AppSkeleton.vue";
import ColorPicker from "@/components/ColorPicker.vue";
import { useConfigStore } from "@/stores/config";

const props = withDefaults(
  defineProps<{
    initialDisplayName?: string | null;
    initialHomeCity?: string | null;
    initialColor?: string | null;
    submitLabel?: string;
  }>(),
  {
    initialDisplayName: null,
    initialHomeCity: null,
    initialColor: null,
    submitLabel: "Save",
  },
);

const emit = defineEmits<{
  submit: [
    payload: { displayName: string | null; homeCity: string | null; color: string },
    done: () => void,
  ];
}>();

const authStore = useAuthStore();
const configStore = useConfigStore();

const palette = computed(() => configStore.config?.colorPalette ?? []);
const regionCountries = computed(() => configStore.config?.regions ?? []);
const colorsInUse = ref<string[]>([]);

const displayName = ref(props.initialDisplayName ?? authStore.getFullName() ?? "");
const homeCity = ref<string | null>(props.initialHomeCity);
const color = ref<string | null>(props.initialColor);
const error = ref<string | null>(null);
const submitting = ref(false);
const configLoading = ref(true);

onMounted(async () => {
  try {
    const [, { colors }] = await Promise.all([configStore.loadOnce(), getColorsInUse()]);
    colorsInUse.value = colors;
  } catch (error: unknown) {
    // The API is unreachable, so we can't know which colors are taken. The form
    // should still preselect one rather than leave the field blank.
    console.error("ERROR => Failed to load profile form config:", error);
  } finally {
    configLoading.value = false;
  }

  if (!color.value) {
    const firstAvailable = palette.value.find((c) => !isTaken(c));
    color.value = firstAvailable ?? palette.value[Math.floor(Math.random() * palette.value.length)];
  }
});

// A color counts as "taken" only if it belongs to someone else. Our own current
// color, when editing, stays selectable.
function isTaken(c: string): boolean {
  return colorsInUse.value.includes(c) && c !== props.initialColor;
}

const canSubmit = computed(() => displayName.value.trim().length > 0 && color.value !== null);

function submit() {
  if (!canSubmit.value || !color.value) return;
  error.value = null;
  submitting.value = true;
  emit(
    "submit",
    {
      displayName: displayName.value.trim() || null,
      homeCity: homeCity.value,
      color: color.value,
    },
    () => {
      submitting.value = false;
    },
  );
}
</script>
