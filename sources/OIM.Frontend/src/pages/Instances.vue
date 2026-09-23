<template>
  <div>
    <div class="entete-page">
      <h1>Instances</h1>
      <BarreRafraichissement v-model="automatique" :chargement="chargement" :derniere-maj="derniereMaj" @actualiser="actualiser" />
    </div>

    <utd-section-filtres-recherche id="filtresInstances" titre="Critères de recherche" extensible="false">
      <form @submit.prevent="rechercher">
        <div class="utd-row">
          <div class="utd-col-auto">
            <utd-champ-form id="champRecherche" libelle="Recherche" precision="Identifiant, statut métier ou valeur d’entrée (ex. numéro de dossier)." format="lg">
              <input v-model="filtre.recherche" type="search" />
            </utd-champ-form>
          </div>
          <div class="utd-col-auto">
            <utd-champ-form id="champStatut" libelle="Statut" format="md">
              <select v-model="filtre.statut">
                <option value="">Tous</option>
                <option value="Running,Pending">En cours</option>
                <option value="Suspended">Suspendues</option>
                <option value="Completed">Terminées</option>
                <option value="Failed">En échec</option>
                <option value="Terminated">Interrompues</option>
              </select>
            </utd-champ-form>
          </div>
          <div class="utd-col-auto">
            <utd-champ-form id="champProcessus" libelle="Processus" format="md">
              <select v-model="filtre.processus">
                <option value="">Tous</option>
                <option v-for="d in definitions" :key="d.id" :value="d.id">{{ d.nom ?? d.id }}</option>
              </select>
            </utd-champ-form>
          </div>
          <div class="utd-col-auto" style="align-self: center">
            <label class="interrupteur">
              <input v-model="filtre.attente" type="checkbox" />
              En attente d’un événement
            </label>
          </div>
        </div>
        <div slot="boutons">
          <button type="submit" class="utd-btn compact primaire" @click="rechercher">Rechercher</button>
          <button type="button" class="utd-btn compact tertiaire comme-lien" @click="reinitialiser">Réinitialiser</button>
        </div>
      </form>
    </utd-section-filtres-recherche>

    <div class="carte mt-16">
      <p class="texte-attenue" aria-live="polite">{{ page?.total ?? 0 }} instance(s)</p>
      <p v-if="page && page.elements.length === 0" class="zone-vide">Aucune instance ne correspond aux critères.</p>
      <table v-else class="utd-table bordures-lignes hover-lignes compact tableau-cliquable">
        <caption class="utd-sr-only">Instances de processus</caption>
        <thead>
          <tr>
            <th scope="col">Instance</th>
            <th scope="col">Processus</th>
            <th scope="col">Statut</th>
            <th scope="col">Statut métier / étape</th>
            <th scope="col">Démarrée</th>
            <th scope="col">Durée</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="i in page?.elements ?? []" :key="i.instanceId" @click="ouvrir(i.instanceId)">
            <td>
              <router-link :to="lien(i.instanceId)" class="texte-mono" @click.stop>{{ i.instanceId }}</router-link>
              <div v-if="i.parentInstanceId" class="texte-attenue">sous-processus</div>
            </td>
            <td class="texte-mono">{{ i.processus }} <span class="texte-attenue">v{{ i.version }}</span></td>
            <td><PastilleStatut :statut="i.statut" /></td>
            <td>
              <div>{{ i.statutMetier ?? '—' }}</div>
              <div class="texte-attenue">
                <span class="texte-mono">{{ i.etape }}</span>
                <template v-if="i.evenementAttendu && i.statut === 'Running'"> · attend « {{ i.evenementAttendu }} »</template>
                <template v-if="i.echeance && i.statut === 'Running'"> · échéance {{ relatif(i.echeance) }}</template>
              </div>
              <div v-if="i.erreur && i.statut === 'Failed'" class="cellule-erreur">{{ i.erreur }}</div>
            </td>
            <td :title="dateHeure(i.creee)">{{ relatif(i.creee) }}</td>
            <td>{{ duree(i.creee, i.terminee) }}</td>
          </tr>
        </tbody>
      </table>

      <div v-if="page && page.total > page.taille" class="pagination">
        <span class="texte-attenue">Page {{ page.page }} de {{ nbPages }}</span>
        <div class="barre-actions">
          <button type="button" class="utd-btn secondaire compact" :disabled="page.page <= 1" @click="allerPage(page.page - 1)">Précédente</button>
          <button type="button" class="utd-btn secondaire compact" :disabled="page.page >= nbPages" @click="allerPage(page.page + 1)">Suivante</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '@/lib/api'
import { dateHeure, duree, relatif } from '@/lib/format'
import type { PageInstances, ResumeDefinition } from '@/lib/types'
import { useRafraichissement } from '@/lib/rafraichissement'
import PastilleStatut from '@/components/PastilleStatut.vue'
import BarreRafraichissement from '@/components/BarreRafraichissement.vue'

const route = useRoute()
const router = useRouter()

const q = route.query
const filtre = reactive({
  recherche: String(q.recherche ?? ''),
  statut: String(q.statut ?? ''),
  processus: String(q.processus ?? ''),
  attente: q.attente === 'true'
})
const numeroPage = ref(Number(q.page ?? 1))
const page = ref<PageInstances | null>(null)
const definitions = ref<ResumeDefinition[]>([])

const nbPages = computed(() => (page.value ? Math.max(1, Math.ceil(page.value.total / page.value.taille)) : 1))

const { automatique, chargement, derniereMaj, actualiser } = useRafraichissement(async () => {
  page.value = await api.instances({ ...filtre, page: numeroPage.value, taille: 25 })
})

onMounted(async () => {
  definitions.value = await api.definitions().catch(() => [])
})

const synchroniserUrl = (): void => {
  const query: Record<string, string> = {}
  for (const [k, v] of Object.entries(filtre)) if (v) query[k] = String(v)
  if (numeroPage.value > 1) query.page = String(numeroPage.value)
  router.replace({ query })
}

const rechercher = (): void => {
  numeroPage.value = 1
  synchroniserUrl()
  actualiser()
}

const reinitialiser = (): void => {
  Object.assign(filtre, { recherche: '', statut: '', processus: '', attente: false })
  rechercher()
}

const allerPage = (n: number): void => {
  numeroPage.value = n
  synchroniserUrl()
  actualiser()
}

const lien = (id: string): string => `/instances/${encodeURIComponent(id)}`
const ouvrir = (id: string): void => {
  router.push(lien(id))
}
</script>
