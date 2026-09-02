<template>
  <div
    v-for="segment in segments"
    :key="segment.start"
    class="absolute top-0 text-center text-[10px] font-bold"
    :class="segment.painted ? 'bg-accent-solid text-on-accent' : ''"
    :style="{
      left: `${nameWidth + segment.start * columnWidth + 1}px`,
      width: `${(segment.end - segment.start + 1) * columnWidth - 1}px`,
      height: `${rowHeight - 1}px`,
      lineHeight: `${rowHeight - 1}px`,
    }"
    role="gridcell"
    :aria-label="absenceTooltip(person, segment.absences, isMine, homeTimeZone)"
  >
    {{ segment.label }}
  </div>
</template>

<script setup lang="ts">
import { type AbsenceSegment, absenceTooltip } from "@/lib/absenceSegments";
import type { PersonDto } from "@/types/person";

// Centered with line-height rather than flex: a segment is a box with one short text run,
// and a plain block with a line-height lays out in a fraction of what a flex container
// costs, which adds up over the few thousand segments a window holds.
//
// The segments of one calendar row, split out of CalendarGrid for one reason: a component
// boundary is what lets Vue skip them. The grid positions rows by transform on its own
// element; when only the order changes, every prop here is identical by identity and Vue
// doesn't re-render a single segment. Inlined in the grid's v-for, the same reorder would
// re-diff every segment of every row in view.
defineProps<{
  person: PersonDto;
  segments: AbsenceSegment[];
  isMine: boolean;
  homeTimeZone: string | undefined;
  nameWidth: number;
  columnWidth: number;
  rowHeight: number;
}>();
</script>
