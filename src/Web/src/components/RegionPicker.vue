<template>
  <div class="flex flex-col gap-5">
    <div class="flex flex-col gap-1.5">
      <label :for="`${idPrefix}Country`" class="text-xs font-semibold text-secondary"
        >Country</label
      >
      <AppSkeleton v-if="countriesLoading" class="h-10 w-full" />
      <RegionSelect
        v-else
        :id="`${idPrefix}Country`"
        v-model="country"
        :options="countries"
        empty-label="Not set"
        placeholder="Search countries…"
      />
    </div>

    <!-- Always present, so the form doesn't grow a field the moment a country is picked.
         There is nothing to pick until then, nor for the majority of countries, whose
         public holidays are all nationwide. -->
    <div class="flex flex-col gap-1.5">
      <label :for="`${idPrefix}Region`" class="text-xs font-semibold text-secondary">Region</label>
      <AppSkeleton v-if="regionsLoading" class="h-10 w-full" />
      <RegionSelect
        v-else
        :id="`${idPrefix}Region`"
        v-model="region"
        :options="regions"
        :disabled="regions.length === 0"
        empty-label="Nationwide holidays only"
        :placeholder="regionPlaceholder"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";

import { getAvailableCountries, getAvailableRegions } from "@/api/regions";
import AppSkeleton from "@/components/AppSkeleton.vue";
import RegionSelect from "@/components/RegionSelect.vue";
import type { RegionOption } from "@/types/config";

/**
 * Picks one ISO 3166 code out of everything the holiday source offers, which is wider than
 * the set the team currently occupies: this is how a region new to the team gets chosen in
 * the first place. Saving the profile is what pulls that region's holidays in.
 */
const props = withDefaults(defineProps<{ modelValue: string | null; idPrefix?: string }>(), {
  idPrefix: "home",
});

const emit = defineEmits<{ "update:modelValue": [value: string | null] }>();

// One stored code, two fields to pick it with. "ES-MD" splits into Spain and Madrid; "ES"
// is Spain with no subdivision, which is all most countries ever have.
const country = ref<string | null>(props.modelValue?.split("-")[0] ?? null);
const region = ref<string | null>(props.modelValue?.includes("-") ? props.modelValue : null);

const countries = ref<RegionOption[]>([]);
const regions = ref<RegionOption[]>([]);
const countriesLoading = ref(true);
const regionsLoading = ref(false);

// The disabled field has to say why it is disabled: there are two different reasons, and
// "no country yet" is the one the reader can do something about.
const regionPlaceholder = computed(() => {
  if (!country.value) return "Pick a country first";
  return regions.value.length > 0 ? "Search regions…" : "Nationwide holidays only";
});

const selected = computed(() => region.value ?? country.value);
watch(selected, (value) => emit("update:modelValue", value));

onMounted(async () => {
  try {
    countries.value = (await getAvailableCountries()).regions;
  } catch (error: unknown) {
    // No source, or it is unreachable. The field stays empty rather than blocking the
    // rest of the form, and whatever was already saved is left untouched.
    console.error("ERROR => Failed to load countries:", error);
  } finally {
    countriesLoading.value = false;
  }
});

watch(
  country,
  async (code, previous) => {
    // Only on an actual change, so arriving with "ES-MD" already saved doesn't clear the
    // subdivision before its list has even loaded.
    if (previous !== undefined && code !== previous) region.value = null;

    regions.value = [];
    if (!code) return;

    regionsLoading.value = true;
    try {
      regions.value = (await getAvailableRegions(code)).regions;
    } catch (error: unknown) {
      // Leaves the country selected with no subdivision, which is still a valid answer.
      console.error("ERROR => Failed to load regions:", error);
    } finally {
      regionsLoading.value = false;
    }
  },
  // Immediate so an existing "ES-MD" arrives with Madrid already selected rather than
  // showing an empty region field until the country is touched.
  { immediate: true },
);
</script>
