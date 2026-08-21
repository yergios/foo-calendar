<template>
  <section>
    <h4>People</h4>

    <div
      class="grid gap-6 mt-4 items-start"
      :class="canManagePeople ? 'grid-cols-[3fr_2fr]' : 'grid-cols-1'"
    >
      <div class="min-w-0 border border-border rounded-12 p-4">
        <div class="flex items-center justify-between gap-3 flex-wrap min-h-9">
          <div class="flex items-center gap-3 flex-wrap">
            <input
              v-model="search"
              type="text"
              placeholder="Search by name…"
              class="h-9 w-56 border border-border-strong rounded-8 px-3 text-sm bg-background"
            />
            <RegionFilterSelect
              id="peopleRegionFilter"
              v-model="regionFilter"
              :groups="regionGroups"
            />
          </div>
          <YearPicker v-if="canReadReports" v-model="statsYear">
            <span class="text-sm text-secondary">Stats for {{ statsYear }}</span>
          </YearPicker>
        </div>

        <p v-if="pageLoading && people.length === 0" class="text-sm text-secondary mt-3">
          Loading…
        </p>

        <table v-else-if="people.length > 0" class="w-full text-sm mt-3">
          <thead>
            <tr class="text-left text-xs font-semibold text-secondary border-b border-border">
              <th class="py-2 pr-2">
                <SortButton
                  :active="sortBy === 'name'"
                  :descending="descending"
                  @toggle="toggleSort('name')"
                  >Person</SortButton
                >
              </th>
              <th class="py-2 pr-2">
                <SortButton
                  :active="sortBy === 'username'"
                  :descending="descending"
                  @toggle="toggleSort('username')"
                  >Username</SortButton
                >
              </th>
              <th class="py-2 pr-2">
                <SortButton
                  :active="sortBy === 'homeCity'"
                  :descending="descending"
                  @toggle="toggleSort('homeCity')"
                  >Home city</SortButton
                >
              </th>
              <th v-if="canReadReports" class="py-2 pr-2 text-center">
                <SortButton
                  :active="sortBy === 'vacation'"
                  :descending="descending"
                  @toggle="toggleSort('vacation')"
                  >Vacation</SortButton
                >
              </th>
              <th v-if="canReadReports" class="py-2 pr-2 text-center">
                <SortButton
                  :active="sortBy === 'sick'"
                  :descending="descending"
                  @toggle="toggleSort('sick')"
                  >Sick</SortButton
                >
              </th>
              <th v-if="canReadReports" class="py-2 pr-2 text-center">
                <SortButton
                  :active="sortBy === 'other'"
                  :descending="descending"
                  @toggle="toggleSort('other')"
                  >Other</SortButton
                >
              </th>
              <th class="py-2 pr-2"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="person in people" :key="person.id" class="border-b border-border">
              <td class="py-2 pr-2">{{ person.displayName ?? person.username }}</td>
              <td class="py-2 pr-2 text-secondary">{{ person.username }}</td>
              <td class="py-2 pr-2 text-secondary">{{ regionLabel(person.homeCity) }}</td>
              <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
                {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.vacationDays ?? 0) }}
              </td>
              <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
                {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.sickDays ?? 0) }}
              </td>
              <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
                {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.otherDays ?? 0) }}
              </td>
              <td v-if="canManagePeople" class="py-2 pr-2 text-right whitespace-nowrap">
                <button
                  type="button"
                  class="text-xs font-semibold text-primary-text hover:text-accent transition-colors"
                  @click="startEdit(person)"
                >
                  Edit
                </button>
                <button
                  type="button"
                  class="text-xs font-semibold text-error hover:opacity-70 transition-opacity ml-3"
                  :disabled="deletingId === person.id"
                  @click="handleDelete(person)"
                >
                  {{ deletingId === person.id ? "Deleting…" : "Delete" }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else class="text-sm text-secondary mt-3">No people yet.</p>

        <!-- Real, server-side pagination, not just a display slice: `people` here is only
             ever one page's worth, fetched fresh from /api/people/paged on every page
             change - see loadPage below. Only shown once there's a second page, so a small
             team never sees it. -->
        <div v-if="total > PEOPLE_PAGE_SIZE" class="flex items-center justify-end gap-3 mt-3">
          <button
            type="button"
            class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40 disabled:hover:text-secondary"
            :disabled="page === 1 || pageLoading"
            @click="page--"
          >
            ‹ Prev
          </button>
          <span class="text-sm text-secondary">Page {{ page }} of {{ totalPages }}</span>
          <button
            type="button"
            class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40 disabled:hover:text-secondary"
            :disabled="page === totalPages || pageLoading"
            @click="page++"
          >
            Next ›
          </button>
        </div>
      </div>

      <div v-if="canManagePeople" class="min-w-0 border border-border rounded-12 p-4">
        <h5>{{ editingId ? "Edit person" : "Add person" }}</h5>
        <div class="grid grid-cols-1 gap-3 mt-3">
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary"
              >Username<span class="text-error ml-0.5" title="Required">*</span></label
            >
            <input
              v-model="form.username"
              type="text"
              maxlength="100"
              placeholder="e.g. jdoe"
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary">First name</label>
            <input
              v-model="form.firstName"
              type="text"
              maxlength="100"
              placeholder="e.g. Jane"
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary">Last name</label>
            <input
              v-model="form.lastName"
              type="text"
              maxlength="100"
              placeholder="e.g. Doe"
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary"
              >Display name<span class="text-error ml-0.5" title="Required">*</span></label
            >
            <input
              v-model="form.displayName"
              type="text"
              maxlength="100"
              placeholder="e.g. Jane D."
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>
          <!-- Keyed so switching to another person, or back to the blank form, rebuilds
               the picker around their region instead of keeping the last one's. -->
          <RegionPicker :key="editingId ?? 'new'" v-model="form.homeCity" id-prefix="admin" />
        </div>

        <p v-if="formError" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-3">
          {{ formError }}
        </p>

        <div class="flex gap-3 mt-4">
          <button
            type="button"
            class="bg-primary text-on-primary border border-transparent rounded-8 px-4 py-2 text-sm transition-all duration-150 ease-out hover:bg-primary-hover disabled:opacity-40"
            :disabled="!canSubmitForm || submittingForm"
            @click="submitForm"
          >
            {{ submittingForm ? "Saving…" : editingId ? "Save changes" : "Add person" }}
          </button>
          <button
            v-if="editingId"
            type="button"
            class="text-sm text-secondary hover:text-ink transition-colors"
            @click="cancelEdit"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, reactive, ref, watch } from "vue";

import { createPerson, deletePerson, getAllPeopleStats, updatePerson } from "@/api/admin";
import { getPeoplePage } from "@/api/people";
import { useAuthStore } from "@/auth";
import RegionFilterSelect from "@/components/RegionFilterSelect.vue";
import RegionPicker from "@/components/RegionPicker.vue";
import SortButton from "@/components/SortButton.vue";
import YearPicker from "@/components/YearPicker.vue";
import { useStaleGuard } from "@/composables/useStaleGuard";
import { groupRegions } from "@/lib/regionGroups";
import { usePeopleStore } from "@/stores/people";
import { Permission } from "@/types/permission";
import type { PeopleSortColumn, PersonAbsenceStatsDto, PersonDto } from "@/types/person";

// Reachable at all with either permission (see adminSections.ts); which pieces of the
// page that actually unlocks - the stats columns, the add/edit/delete form - is still
// decided permission by permission below.
const authStore = useAuthStore();
const canManagePeople = computed(() => authStore.can(Permission.peopleManage));
const canReadReports = computed(() => authStore.can(Permission.reportsRead));

// Declared up here, not down by loadStats below, because loadPage's query also needs it
// (for the stats-sorted path, see GetStatsSortedPageAsync) and comes first in the file.
const statsYear = ref(new Date().getFullYear());

// Also backs HomeView's calendar (which needs the *whole* roster, unlike this page) and,
// here, just the region filter's options and labels - loaded for that reason alone, kept
// in sync after a create/update/delete so a newly-picked region shows up in the filter
// without a reload. This view's own table is paginated separately below and never reads
// people from this store.
const peopleStore = usePeopleStore();
const { regionLabels } = storeToRefs(peopleStore);
const regionGroups = computed(() => groupRegions(regionLabels.value));

// Falls back to the bare code so a region that isn't in the team's current catalog (yet,
// or anymore) still shows something, same as HomeView's own regionLabel.
function regionLabel(code: string | null): string {
  if (!code) return "—";
  return regionLabels.value[code] ?? code;
}

// Real pagination, not a display slice of an already-fetched full roster: `people` here is
// only ever one page's worth, fetched fresh from /api/people/paged on every page change.
// Person records are small, so this isn't really about the network transfer; it's the same
// reason CalendarGrid's own rows just got fixed - an unbounded HTML table doesn't scale,
// so this never mounts more than one page's worth of <tr>s in the first place.
const PEOPLE_PAGE_SIZE = 25;
const page = ref(1);
const total = ref(0);
const people = ref<PersonDto[]>([]);
const pageLoading = ref(true);
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PEOPLE_PAGE_SIZE)));

const isLatestPageLoad = useStaleGuard();

// Search, region filter, and sort all narrow or reorder the same underlying set, so they
// all have to be applied server-side, before paging - not after, which would just be
// slicing whatever 25 rows happened to load. See PersonService.GetPageAsync.
const search = ref("");
const regionFilter = ref<string[]>([]);
const sortBy = ref<PeopleSortColumn>("name");
const descending = ref(false);
const STATS_SORT_COLUMNS: PeopleSortColumn[] = ["vacation", "sick", "other"];

function toggleSort(column: PeopleSortColumn) {
  if (sortBy.value === column) {
    descending.value = !descending.value;
  } else {
    sortBy.value = column;
    descending.value = false;
  }
}

async function loadPage() {
  const isCurrent = isLatestPageLoad();
  pageLoading.value = true;
  try {
    const result = await getPeoplePage(page.value, PEOPLE_PAGE_SIZE, {
      search: search.value.trim() || undefined,
      regions: regionFilter.value.length > 0 ? regionFilter.value : undefined,
      sortBy: sortBy.value,
      descending: descending.value,
      statsYear: canReadReports.value ? statsYear.value : undefined,
    });
    if (!isCurrent()) return;
    people.value = result.people;
    total.value = result.total;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people:", error);
  } finally {
    if (isCurrent()) pageLoading.value = false;
  }
}

watch(page, loadPage);

// Any of these changing means page 1 might not even be the same 25 people any more, so
// they all jump back to it rather than staying wherever the user happened to be. Setting
// `page` re-triggers the watch above when it was already 1 (a no-op reassignment doesn't),
// so loadPage() is still called directly in that branch.
function reloadFromStart() {
  if (page.value === 1) loadPage();
  else page.value = 1;
}

watch([regionFilter, sortBy, descending], reloadFromStart);

// Debounced, unlike the region filter and sort: those are discrete clicks, this fires on
// every keystroke, and a request per keystroke would just make the later ones win a race
// against each other for no benefit.
let searchDebounce: ReturnType<typeof setTimeout> | undefined;
watch(search, () => {
  clearTimeout(searchDebounce);
  searchDebounce = setTimeout(reloadFromStart, 300);
});

// Only the stats-sorted path (see GetStatsSortedPageAsync) actually depends on the year;
// reloading for every other sort would be a wasted request.
watch(statsYear, () => {
  if (STATS_SORT_COLUMNS.includes(sortBy.value)) reloadFromStart();
});

// A delete can leave the current page past the new end (e.g. deleting the last person on
// the last page), which would otherwise show an empty table with no way back. Just moves
// `page`, not a direct loadPage() call - the watch above is what actually reloads, so the
// two never race each other.
watch(totalPages, (max) => {
  if (page.value > max) page.value = max;
});

const editingId = ref<number | null>(null);
const deletingId = ref<number | null>(null);
const formError = ref<string | null>(null);
const submittingForm = ref(false);

const form = reactive({
  username: "",
  firstName: "",
  lastName: "",
  displayName: "",
  homeCity: null as string | null,
});

const canSubmitForm = computed(() => {
  if (!form.displayName.trim()) return false;
  if (editingId.value) return true;
  return form.username.trim().length > 0;
});

const statsByPerson = ref<Map<number, PersonAbsenceStatsDto>>(new Map());
const statsLoading = ref(true);

const isLatestStatsLoad = useStaleGuard();

async function loadStats() {
  const isCurrent = isLatestStatsLoad();
  statsLoading.value = true;
  try {
    const { stats } = await getAllPeopleStats(statsYear.value);
    if (!isCurrent()) return;
    statsByPerson.value = new Map(stats.map((s) => [s.personId, s]));
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people stats:", error);
  } finally {
    if (isCurrent()) statsLoading.value = false;
  }
}

watch(statsYear, loadStats);

onMounted(() => {
  loadPage();
  if (canReadReports.value) loadStats();
  peopleStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load people:", error);
  });
});

function resetForm() {
  form.username = "";
  form.firstName = "";
  form.lastName = "";
  form.displayName = "";
  form.homeCity = null;
}

function startEdit(person: PersonDto) {
  editingId.value = person.id;
  formError.value = null;
  form.displayName = person.displayName ?? "";
  form.homeCity = person.homeCity;
}

function cancelEdit() {
  editingId.value = null;
  formError.value = null;
  resetForm();
}

async function submitForm() {
  if (!canSubmitForm.value) return;

  formError.value = null;
  submittingForm.value = true;
  try {
    if (editingId.value) {
      await updatePerson(editingId.value, {
        displayName: form.displayName.trim(),
        homeCity: form.homeCity,
      });
    } else {
      await createPerson({
        username: form.username.trim(),
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        displayName: form.displayName.trim(),
        homeCity: form.homeCity,
      });
    }
    cancelEdit();
    // Both: this page's table needs the change, and HomeView's calendar - reading from the
    // store, not this fetch - needs it too, whenever it's next opened.
    await Promise.all([loadPage(), peopleStore.refresh()]);
  } catch (error: unknown) {
    console.error("ERROR => Failed to save person:", error);
    formError.value = error instanceof Error ? error.message : "Failed to save person.";
  } finally {
    submittingForm.value = false;
  }
}

async function handleDelete(person: PersonDto) {
  if (!confirm(`Delete ${person.displayName ?? person.username}?`)) return;

  deletingId.value = person.id;
  try {
    await deletePerson(person.id);
    if (editingId.value === person.id) cancelEdit();
    await Promise.all([loadPage(), peopleStore.refresh()]);
  } catch (error: unknown) {
    console.error("ERROR => Failed to delete person:", error);
  } finally {
    deletingId.value = null;
  }
}
</script>
