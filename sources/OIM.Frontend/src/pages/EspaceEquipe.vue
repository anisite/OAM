<template>
  <div>
    <utd-avis v-if="introuvable" type="erreur" titre="Équipe introuvable.">
      <p>L’équipe « {{ equipe }} » n’existe pas. <router-link to="/">Choisir une équipe</router-link></p>
    </utd-avis>
    <template v-else>
      <utd-avis v-if="detail && !detail.actif" type="avertissement" titre="Cette équipe est désactivée.">
        <p>Aucune nouvelle instance ne peut y être démarrée.</p>
      </utd-avis>
      <router-view :key="$route.path" />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, ErreurApi } from '@/lib/api'
import { useFilAriane, type ElementFil } from '@/lib/filAriane'
import type { EquipeDetail } from '@/lib/types'

const props = defineProps<{ equipe: string }>()
const route = useRoute()

const detail = ref<EquipeDetail | null>(null)
const introuvable = ref(false)

const SECTIONS: Record<string, string> = { instances: 'Instances', processus: 'Processus', concepteur: 'Concepteur' }

/** Accueil › équipe › section › élément (le dernier n'est pas un lien). */
const fil = computed(() => {
  const racine = `/${encodeURIComponent(props.equipe)}`
  const [section, id] = route.path.split('/').filter(Boolean).slice(1)
  const elements: ElementFil[] = [
    { libelle: 'Accueil', lien: '/' },
    { libelle: detail.value?.nom ?? props.equipe, lien: racine }
  ]
  if (section) elements.push({ libelle: SECTIONS[section] ?? section, lien: `${racine}/${section}` })
  if (section && id) elements.push({ libelle: decodeURIComponent(id) })
  delete elements[elements.length - 1]!.lien
  return elements
})
useFilAriane(() => fil.value)

watch(
  () => props.equipe,
  async (id) => {
    detail.value = null
    introuvable.value = false
    try {
      detail.value = await api.equipe(id)
    } catch (e) {
      if (e instanceof ErreurApi && e.statut === 404) introuvable.value = true
      else throw e
    }
  },
  { immediate: true }
)
</script>
