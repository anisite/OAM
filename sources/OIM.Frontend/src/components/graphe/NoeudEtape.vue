<template>
  <div
    class="noeud-etape"
    :class="{
      selectionne: selected,
      courant: data.courant,
      visite: data.mode === 'suivi' && (data.visites ?? 0) > 0,
      'non-visite': data.mode === 'suivi' && !data.visites && !data.courant,
      'en-echec': data.enEchec,
      invalide: data.nbErreurs
    }"
    :style="{ borderColor: info.couleur }"
  >
    <Handle id="entree" type="target" :position="Position.Top" class="poignee-entree" :connectable="edition" />

    <div class="noeud-entete" :style="{ backgroundColor: `${info.couleur}14`, color: info.couleur }">
      <span class="noeud-entete-type">
        <span class="icone-etape" aria-hidden="true">{{ info.icone }}</span>
        {{ info.libelle }}
      </span>
      <button
        v-if="edition"
        type="button"
        class="noeud-btn-suppr"
        :title="`Supprimer l’étape ${e.id}`"
        @click.stop="data.onSupprimer?.(e.uid)"
      >
        <span aria-hidden="true">✕</span><span class="utd-sr-only">Supprimer l’étape {{ e.id }}</span>
      </button>
    </div>

    <div class="noeud-corps">
      <div class="noeud-titre" :title="e.id">{{ e.id }}</div>
      <div v-if="resume" class="noeud-resume texte-mono" :title="resume">{{ resume }}</div>
      <div v-if="e.statut" class="noeud-statut" :title="e.statut">« {{ e.statut }} »</div>
      <div class="noeud-pastilles">
        <span v-if="data.depart" class="noeud-puce depart">Départ</span>
        <span v-if="e.fin" class="noeud-puce fin">Fin</span>
        <span v-if="e.retry" class="noeud-puce" :title="`${e.retry.tentatives} tentatives, délai ${e.retry.delai}, backoff ${e.retry.backoff}`">
          ↻ {{ e.retry.tentatives }}×
        </span>
        <span v-if="e.message" class="noeud-puce" title="Message retourné au client">💬 message</span>
        <span v-if="data.visites" class="noeud-puce visites">✓ {{ data.visites > 1 ? `${data.visites}×` : 'visitée' }}</span>
        <span v-if="data.nbErreurs" class="noeud-puce" style="color: #cb381f">⚠ {{ data.nbErreurs }}</span>
      </div>
    </div>

    <div v-if="conditionnelles.length" class="noeud-branches" aria-hidden="true">
      <span v-for="(b, i) in conditionnelles" :key="i" :title="b.si ?? ''">{{ conditionLisible(b.si ?? '') || '…' }}</span>
      <span style="color: #6b778a">sinon</span>
    </div>

    <!-- Sorties : branches conditionnelles, puis suivant/sinon -->
    <template v-if="!e.fin">
      <Handle
        v-for="(_, i) in conditionnelles"
        :id="`b${i}`"
        :key="`b${i}`"
        type="source"
        :position="Position.Bottom"
        class="poignee-branche"
        :connectable="edition"
        :style="{ left: `${positionBranche(i)}%` }"
      />
      <Handle
        id="suivant"
        type="source"
        :position="Position.Bottom"
        class="poignee-suivant"
        :connectable="edition"
        :style="{ left: conditionnelles.length ? `${positionBranche(conditionnelles.length)}%` : '50%' }"
      />
    </template>

    <Handle v-if="edition || e.siErreur" id="erreur" type="source" :position="Position.Right" class="poignee-erreur" :connectable="edition" style="top: 40%" />
    <Handle
      v-if="info.delaiExpire"
      id="delai"
      type="source"
      :position="Position.Right"
      class="poignee-delai"
      :connectable="edition"
      style="top: 70%"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import { typeEtape } from '@/lib/catalogue'
import { conditionLisible } from '@/lib/graphe'
import type { EtapeModele } from '@/lib/modele'

export interface DonneesNoeud {
  etape: EtapeModele
  mode: 'suivi' | 'edition'
  depart?: boolean
  visites?: number
  courant?: boolean
  enEchec?: boolean
  nbErreurs?: number
  onSupprimer?: (uid: string) => void
}

const props = defineProps<{ id: string; data: DonneesNoeud; selected?: boolean }>()

const e = computed(() => props.data.etape)
const edition = computed(() => props.data.mode === 'edition')
const info = computed(() => typeEtape(e.value.type))
const conditionnelles = computed(() => (e.value.fin ? [] : e.value.suivant.filter((b) => b.si !== null)))

const resume = computed(() => {
  const p = e.value.props
  switch (e.value.type) {
    case 'http': return p.requete
    case 'attendreEvenement': return `${p.evenement ?? '?'}${p.delai ? ` · ${p.delai}` : ''}`
    case 'courriel': return `${p.gabarit ?? '?'}${p.a ? ` → ${p.a}` : ''}`
    case 'delai': return p.duree ?? p.jusqua
    case 'sousProcessus': return p.processus
    case 'definir': return Object.keys(p.variables ?? {}).join(', ')
    case 'reponse': return `HTTP ${p.statutHttp ?? 200} · ${Object.keys(p.corps ?? {}).join(', ')}`
    default: return ''
  }
})

const positionBranche = (i: number): number => {
  const total = conditionnelles.value.length + 1
  return 15 + (i / Math.max(1, total - 1)) * 70
}
</script>
