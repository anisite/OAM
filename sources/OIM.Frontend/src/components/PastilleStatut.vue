<template>
  <span class="utd-pastille" :class="info.couleur">{{ info.libelle }}</span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { STATUTS } from '@/lib/format'

const props = defineProps<{ statut: string }>()
const info = computed(() => STATUTS[props.statut] ?? { libelle: props.statut, couleur: 'gris' })
</script>
