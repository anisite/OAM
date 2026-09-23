<template>
  <div>
    <div class="entete-page">
      <h1>Tableau de bord</h1>
      <BarreRafraichissement v-model="automatique" :chargement="chargement" :derniere-maj="derniereMaj" @actualiser="actualiser" />
    </div>

    <utd-avis v-if="erreur" type="erreur" titre="Le serveur OIM ne répond pas.">
      <p>{{ erreur }}</p>
    </utd-avis>

    <!-- Compteurs -->
    <ul class="tuiles-compteurs" aria-label="Indicateurs">
      <li v-for="t in tuiles" :key="t.libelle">
        <router-link :to="t.lien" class="tuile-compteur" :class="t.classe">
          <span class="tuile-compteur-valeur">{{ t.valeur }}</span>
          <span class="tuile-compteur-libelle">{{ t.libelle }}</span>
        </router-link>
      </li>
    </ul>

    <div class="grille-tableau-bord">
      <!-- Par processus -->
      <section class="carte" aria-labelledby="titreParProcessus">
        <div class="carte-entete">
          <h2 id="titreParProcessus">Instances par processus</h2>
          <router-link to="/processus" class="utd-btn tertiaire comme-lien compact">Tous les processus</router-link>
        </div>
        <p v-if="parProcessus.length === 0" class="zone-vide">
          Aucune instance pour l’instant. Démarrez un processus depuis la page
          <router-link to="/processus">Processus</router-link>.
        </p>
        <table v-else class="utd-table bordures-lignes hover-lignes compact">
          <caption class="utd-sr-only">Nombre d’instances par processus et par statut</caption>
          <thead>
            <tr>
              <th scope="col">Processus</th>
              <th v-for="s in colonnesStatut" :key="s" scope="col" class="colonne-nombre">
                <PastilleStatut :statut="s" />
              </th>
              <th scope="col" class="colonne-nombre">Total</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in parProcessus" :key="p.processus">
              <th scope="row">
                <router-link :to="`/processus/${encodeURIComponent(p.processus)}`" class="texte-mono">{{ p.processus }}</router-link>
              </th>
              <td v-for="s in colonnesStatut" :key="s" class="colonne-nombre">
                <router-link v-if="p.statuts[s]" :to="{ path: '/instances', query: { processus: p.processus, statut: s } }">
                  {{ p.statuts[s] }}
                </router-link>
                <span v-else class="texte-attenue">·</span>
              </td>
              <td class="colonne-nombre"><strong>{{ p.total }}</strong></td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- Attentes -->
      <section class="carte" aria-labelledby="titreAttentes">
        <div class="carte-entete">
          <h2 id="titreAttentes">En attente d’un événement</h2>
          <router-link :to="{ path: '/instances', query: { attente: 'true' } }" class="utd-btn tertiaire comme-lien compact">
            Voir tout
          </router-link>
        </div>
        <p v-if="!stats?.attentesProches.length" class="zone-vide">Aucune instance en attente.</p>
        <ul v-else class="liste-instances">
          <li v-for="i in stats.attentesProches" :key="i.instanceId">
            <router-link :to="lienInstance(i.instanceId)" class="liste-instances-lien">
              <span class="liste-instances-titre">
                <span class="texte-mono">{{ i.instanceId }}</span>
                <span class="utd-etiquette">{{ i.evenementAttendu }}</span>
              </span>
              <span class="texte-attenue">
                {{ i.processus }} · {{ i.statutMetier ?? i.etape }}
                <template v-if="i.echeance"> · échéance {{ relatif(i.echeance) }}</template>
              </span>
            </router-link>
          </li>
        </ul>
      </section>

      <!-- Échecs -->
      <section class="carte carte-large" aria-labelledby="titreEchecs">
        <div class="carte-entete">
          <h2 id="titreEchecs">Échecs récents</h2>
          <router-link :to="{ path: '/instances', query: { statut: 'Failed' } }" class="utd-btn tertiaire comme-lien compact">
            Voir tout
          </router-link>
        </div>
        <p v-if="!stats?.echecsRecents.length" class="zone-vide">Aucun échec. 🎉</p>
        <table v-else class="utd-table bordures-lignes hover-lignes compact">
          <caption class="utd-sr-only">Instances en échec les plus récentes</caption>
          <thead>
            <tr>
              <th scope="col">Instance</th>
              <th scope="col">Processus</th>
              <th scope="col">Erreur</th>
              <th scope="col">Depuis</th>
              <th scope="col"><span class="utd-sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="i in stats.echecsRecents" :key="i.instanceId">
              <td><router-link :to="lienInstance(i.instanceId)" class="texte-mono">{{ i.instanceId }}</router-link></td>
              <td class="texte-mono">{{ i.processus }} v{{ i.version }}</td>
              <td class="cellule-erreur">{{ i.erreur ?? '—' }}</td>
              <td>{{ relatif(i.miseAJour) }}</td>
              <td class="cellule-actions">
                <button type="button" class="utd-btn secondaire compact" @click="relancer(i.instanceId)">
                  Relancer<span class="utd-sr-only"> {{ i.instanceId }}</span>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '@/lib/api'
import { relatif } from '@/lib/format'
import type { Statistiques } from '@/lib/types'
import { useRafraichissement } from '@/lib/rafraichissement'
import PastilleStatut from '@/components/PastilleStatut.vue'
import BarreRafraichissement from '@/components/BarreRafraichissement.vue'
import MessagesHelpers from '@/helpers/messages'

const stats = ref<Statistiques | null>(null)
const erreur = ref('')

const { automatique, chargement, derniereMaj, actualiser } = useRafraichissement(async () => {
  try {
    stats.value = await api.tableauDeBord()
    erreur.value = ''
  } catch (e) {
    erreur.value = (e as Error).message
  }
})

const n = (s: string): number => stats.value?.parStatut[s] ?? 0

const tuiles = computed(() => [
  { libelle: 'En cours', valeur: n('Running') + n('Pending'), lien: { path: '/instances', query: { statut: 'Running,Pending' } }, classe: 'bleu' },
  { libelle: 'En attente d’un événement', valeur: stats.value?.enAttenteEvenement ?? 0, lien: { path: '/instances', query: { attente: 'true' } }, classe: 'ocre' },
  { libelle: 'Suspendues', valeur: n('Suspended'), lien: { path: '/instances', query: { statut: 'Suspended' } }, classe: 'jaune' },
  { libelle: 'Démarrées (24 h)', valeur: stats.value?.demarrees24h ?? 0, lien: { path: '/instances' }, classe: '' },
  { libelle: 'Terminées (24 h)', valeur: stats.value?.terminees24h ?? 0, lien: { path: '/instances', query: { statut: 'Completed' } }, classe: 'vert' },
  { libelle: 'En échec (24 h)', valeur: stats.value?.echouees24h ?? 0, lien: { path: '/instances', query: { statut: 'Failed' } }, classe: 'rouge' }
])

const colonnesStatut = ['Running', 'Suspended', 'Completed', 'Failed', 'Terminated']

const parProcessus = computed(() => {
  const map = new Map<string, { processus: string; statuts: Record<string, number>; total: number }>()
  for (const l of stats.value?.parProcessus ?? []) {
    const e = map.get(l.processus) ?? { processus: l.processus, statuts: {}, total: 0 }
    const cle = l.statut === 'Pending' ? 'Running' : l.statut
    e.statuts[cle] = (e.statuts[cle] ?? 0) + l.nombre
    e.total += l.nombre
    map.set(l.processus, e)
  }
  return [...map.values()].sort((a, b) => a.processus.localeCompare(b.processus))
})

const lienInstance = (id: string): string => `/instances/${encodeURIComponent(id)}`

const relancer = async (id: string): Promise<void> => {
  try {
    await api.commande(id, 'relancer')
    MessagesHelpers.notifierSucces(`L’instance « ${id} » a été relancée à partir de l’étape en échec.`)
    await actualiser()
  } catch (e) {
    MessagesHelpers.notifierErreur((e as Error).message)
  }
}
</script>
