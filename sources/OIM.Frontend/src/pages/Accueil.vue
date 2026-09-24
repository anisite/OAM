<template>
  <div>
    <div class="entete-page">
      <h1>Choisir une équipe</h1>
      <router-link v-if="moi?.admin" to="/admin/equipes" class="utd-btn secondaire compact">Administrer les équipes</router-link>
    </div>

    <utd-avis v-if="refus" type="erreur" titre="Accès refusé.">
      <p>{{ refus }}</p>
      <p>Demandez à un administrateur OIM de vous ajouter à une équipe (votre compte ou un de vos groupes).</p>
    </utd-avis>

    <template v-else-if="equipes">
      <utd-champ-form v-if="equipes.length > 8" id="champRechercheEquipe" libelle="Rechercher une équipe" format="lg">
        <input v-model="recherche" type="search" />
      </utd-champ-form>

      <p v-if="equipes.length === 0" class="zone-vide">
        Aucune équipe.
        <router-link v-if="moi?.admin" to="/admin/equipes">Créer une équipe</router-link>
      </p>
      <p v-else-if="visibles.length === 0" class="zone-vide">Aucune équipe ne correspond à « {{ recherche }} ».</p>

      <!-- Tuiles UTD : le clic sur une tuile déclenche le lien qu'elle contient (navigation sans rechargement).
           Recréées quand la liste change : le conteneur égalise la hauteur des tuiles à l'affichage. -->
      <utd-tuile-conteneur v-else :key="cleTuiles" sr-titre="Équipes" tag-titre="h2" nb-colonnes-max="4" couleur-fond="transparent">
        <utd-tuile
          v-for="e in visibles"
          :key="e.id"
          :titre="e.nom"
          :href="`/${encodeURIComponent(e.id)}`"
          :description="e.description || e.id"
          :description2="compteurs(e)"
          :description3="e.actif ? '' : 'Équipe désactivée'"
        >
          <router-link :to="`/${encodeURIComponent(e.id)}`" class="utd-d-none" tabindex="-1" aria-hidden="true">{{ e.nom }}</router-link>
        </utd-tuile>
      </utd-tuile-conteneur>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ErreurApi } from '@/lib/api'
import { chargerMoi, moi } from '@/lib/session'
import type { ResumeEquipe } from '@/lib/types'

const router = useRouter()
const equipes = ref<ResumeEquipe[] | null>(null)
const recherche = ref('')
const refus = ref('')

const normaliser = (t: string): string => t.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

const visibles = computed(() => {
  const r = normaliser(recherche.value.trim())
  return (equipes.value ?? []).filter((e) => !r || normaliser(`${e.nom} ${e.id} ${e.description ?? ''}`).includes(r))
})
const cleTuiles = computed(() => visibles.value.map((e) => e.id).join('|'))

const compteurs = (e: ResumeEquipe): string =>
  `${e.compteurs.actives} active(s) · ${e.compteurs.enAttente} en attente · ${e.compteurs.echecs} en échec`

onMounted(async () => {
  try {
    const m = await chargerMoi()
    equipes.value = await api.equipes()
    // Une seule équipe : on y va directement.
    if (!m.admin && !m.support && equipes.value.length === 1) router.replace(`/${encodeURIComponent(equipes.value[0]!.id)}`)
  } catch (e) {
    if (e instanceof ErreurApi && (e.statut === 403 || e.statut === 401)) refus.value = e.message
    else throw e
  }
})
</script>
