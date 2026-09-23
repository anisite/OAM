<template>
  <div>
    <div class="zone-graphe" :style="{ height }">
      <VueFlow
        :id="idFlux"
        :nodes="noeuds"
        :edges="liens"
        :nodes-draggable="false"
        :nodes-connectable="false"
        :elements-selectable="false"
        :min-zoom="0.2"
        :max-zoom="1.5"
        fit-view-on-init
        @nodes-initialized="recadrer"
      >
        <template #node-etape="p">
          <NoeudEtape :id="p.id" :data="p.data" />
        </template>
        <Background :gap="24" :size="1.2" pattern-color="#cfd8e3" />
        <Controls :show-interactive="false" />
      </VueFlow>
    </div>
    <div class="legende-graphe" aria-hidden="true">
      <span><i class="legende-trait" :style="{ background: COULEURS_LIENS.suivant }"></i>Suivant</span>
      <span><i class="legende-trait" :style="{ background: COULEURS_LIENS.branche }"></i>Condition</span>
      <span><i class="legende-trait" :style="{ background: COULEURS_LIENS.sinon }"></i>Sinon</span>
      <span><i class="legende-trait" :style="{ background: COULEURS_LIENS.erreur }"></i>Si erreur</span>
      <span><i class="legende-trait" :style="{ background: COULEURS_LIENS.delai }"></i>Délai expiré</span>
      <span v-if="parcours"><i class="legende-trait" :style="{ background: COULEURS_LIENS.parcouru, height: '4px' }"></i>Chemin parcouru</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { VueFlow, useVueFlow, type Node } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import NoeudEtape, { type DonneesNoeud } from './NoeudEtape.vue'
import { COULEURS_LIENS } from '@/lib/catalogue'
import { aretes, disposer, transitionsParcourues } from '@/lib/graphe'
import type { ModeleProcessus } from '@/lib/modele'

const props = withDefaults(
  defineProps<{
    modele: ModeleProcessus
    /** Étapes visitées dans l'ordre (statut personnalisé « parcours »). */
    parcours?: string[]
    etapeCourante?: string
    enEchec?: boolean
    height?: string
  }>(),
  { height: '560px' }
)

const idFlux = `graphe-${Math.random().toString(36).slice(2)}`
const { fitView } = useVueFlow(idFlux)

// Positions conservées tant que la structure ne change pas (l'utilisateur peut déplacer les noeuds).
const positions = ref<Record<string, { x: number; y: number }>>({})
watch(
  () => props.modele.etapes.map((e) => e.uid).join(),
  () => (positions.value = disposer(props.modele)),
  { immediate: true }
)

const visites = computed(() => {
  const n: Record<string, number> = {}
  for (const id of props.parcours ?? []) n[id] = (n[id] ?? 0) + 1
  return n
})

const noeuds = computed<Node<DonneesNoeud>[]>(() =>
  props.modele.etapes.map((etape, i) => ({
    id: etape.uid,
    type: 'etape',
    position: positions.value[etape.uid] ?? { x: 0, y: i * 140 },
    data: {
      etape,
      mode: 'suivi',
      depart: i === 0,
      visites: props.parcours ? visites.value[etape.id] ?? 0 : undefined,
      courant: props.etapeCourante === etape.id && !props.enEchec,
      enEchec: props.enEchec && props.etapeCourante === etape.id
    }
  }))
)

const liens = computed(() => aretes(props.modele, transitionsParcourues(props.parcours ?? [])))

const recadrer = async (): Promise<void> => {
  await nextTick()
  fitView({ padding: 0.15, maxZoom: 1 })
}
</script>
