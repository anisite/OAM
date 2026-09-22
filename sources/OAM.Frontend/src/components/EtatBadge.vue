<template>
  <span :class="['etat-badge', `etat-${etatStr.toLowerCase()}`]">{{ libelle }}</span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({ etat: { type: [String, Number], required: true } })

const etatNoms = ['EnAttente', 'EnCours', 'EnPause', 'EnErreur', 'Termine', 'Annule']
const etatStr = computed(() => typeof props.etat === 'number' ? (etatNoms[props.etat] ?? String(props.etat)) : props.etat)

const libelles = {
  EnAttente: 'En attente',
  EnCours: 'En cours',
  EnPause: 'En pause',
  EnErreur: 'En erreur',
  Termine: 'Terminé',
  Annule: 'Annulé',
  Reussie: 'Réussie',
  Ignoree: 'Ignorée'
}

const libelle = computed(() => libelles[etatStr.value] || etatStr.value)
</script>

