<template>
  <span :class="['etat-badge', `etat-${etat.toLowerCase()}`]">{{ libelle }}</span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({ etat: { type: String, required: true } })

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

const libelle = computed(() => libelles[props.etat] || props.etat)
</script>

<style scoped>
.etat-badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 600;
}
.etat-enattente { background: #e0e0e0; color: #555; }
.etat-encours { background: #bbdefb; color: #1565c0; }
.etat-enpause { background: #fff9c4; color: #f57f17; }
.etat-enerreur { background: #ffcdd2; color: #c62828; }
.etat-termine, .etat-reussie { background: #c8e6c9; color: #2e7d32; }
.etat-annule, .etat-ignoree { background: #f5f5f5; color: #999; }
</style>
