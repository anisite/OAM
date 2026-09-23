<template>
  <div>
    <p class="mb-8"><router-link to="/instances">← Retour aux instances</router-link></p>

    <utd-avis v-if="erreurChargement" type="erreur" titre="Instance introuvable.">
      <p>{{ erreurChargement }}</p>
    </utd-avis>

    <template v-if="instance">
      <div class="entete-page">
        <div>
          <div class="entete-page-titre">
            <h1 class="texte-mono">{{ instance.instanceId }}</h1>
            <PastilleStatut :statut="instance.statut" />
          </div>
          <p class="sous-titre">
            Processus
            <router-link :to="`/processus/${encodeURIComponent(instance.processus)}`" class="texte-mono">{{ instance.processus }}</router-link>
            v{{ instance.version }}
            <template v-if="instance.parentInstanceId">
              · sous-processus de
              <router-link :to="`/instances/${encodeURIComponent(instance.parentInstanceId)}`" class="texte-mono">{{ instance.parentInstanceId }}</router-link>
            </template>
          </p>
        </div>
        <BarreRafraichissement v-model="automatique" :chargement="chargement" :derniere-maj="derniereMaj" @actualiser="actualiser" />
      </div>

      <!-- Actions de pilotage selon le statut -->
      <div class="barre-actions mb-16">
        <button v-if="actif" type="button" class="utd-btn primaire compact" @click="modaleEvenement = true">Envoyer un événement</button>
        <button v-if="instance.statut === 'Running'" type="button" class="utd-btn secondaire compact" @click="commande('suspendre')">Suspendre</button>
        <button v-if="instance.statut === 'Suspended'" type="button" class="utd-btn secondaire compact" @click="commande('reprendre')">Reprendre</button>
        <button v-if="instance.statut === 'Failed'" type="button" class="utd-btn primaire compact" @click="commande('relancer')">Relancer l’étape en échec</button>
        <button v-if="!actif" type="button" class="utd-btn secondaire compact" @click="redemarrer">Redémarrer avec les mêmes entrées</button>
        <button v-if="actif" type="button" class="utd-btn avertissement compact" @click="commande('terminer')">Interrompre</button>
        <button v-if="!actif" type="button" class="utd-btn avertissement compact" @click="purger">Supprimer</button>
      </div>

      <utd-avis v-if="instance.erreur" type="erreur" :titre="instance.statut === 'Terminated' ? 'Instance interrompue' : 'Instance en échec'">
        <p>{{ instance.erreur.message }}</p>
      </utd-avis>

      <!-- Suivi métier : ce que voit aussi l'application cliente -->
      <div class="statut-metier">
        <div class="statut-metier-libelle">{{ sp?.statut ?? 'Aucun statut métier' }}</div>
        <div v-if="sp?.message" class="statut-metier-message">💬 {{ sp.message }}</div>
        <div class="texte-attenue mt-8">
          Étape <span class="texte-mono">{{ sp?.etape ?? '—' }}</span>
          <template v-if="sp?.attente?.evenement && actif"> · attend l’événement « <strong>{{ sp.attente.evenement }}</strong> »</template>
          <template v-if="sp?.attente?.echeance && actif"> · échéance {{ dateHeure(sp.attente.echeance) }} ({{ relatif(sp.attente.echeance) }})</template>
          · {{ sp?.transitions ?? 0 }} transition(s)
        </div>
      </div>

      <dl class="grille-infos mb-16">
        <div><dt>Démarrée</dt><dd>{{ dateHeure(instance.creee) }}</dd></div>
        <div><dt>Dernière activité</dt><dd>{{ dateHeure(instance.miseAJour) }}</dd></div>
        <div><dt>Terminée</dt><dd>{{ dateHeure(instance.terminee) }}</dd></div>
        <div><dt>Durée</dt><dd>{{ duree(instance.creee, instance.terminee) }}</dd></div>
        <div><dt>Démarrée par</dt><dd>{{ instance.etiquettes?.demarrePar ?? '—' }}</dd></div>
      </dl>

      <utd-onglets id="ongletsInstance" titre="Détail de l’instance" conserver-etat-affichage="session">
        <utd-onglet id="ongletParcours" titre="Parcours">
          <div class="onglet-contenu">
            <GrapheProcessus
              v-if="modele"
              :modele="modele"
              :parcours="sp?.parcours ?? []"
              :etape-courante="actif || instance.statut === 'Failed' ? sp?.etape : undefined"
              :en-echec="instance.statut === 'Failed'"
            />
            <p v-else class="zone-vide">Chargement de la définition…</p>
          </div>
        </utd-onglet>
        <utd-onglet id="ongletDonnees" titre="Données">
          <div class="onglet-contenu grille-2">
            <div>
              <h2 class="h3">Entrées</h2>
              <pre class="bloc-json">{{ json(instance.entrees) }}</pre>
            </div>
            <div>
              <h2 class="h3">Sortie</h2>
              <pre class="bloc-json">{{ json(instance.sortie) }}</pre>
            </div>
            <div>
              <h2 class="h3">Statut personnalisé</h2>
              <pre class="bloc-json">{{ json(instance.statutPersonnalise) }}</pre>
            </div>
            <div v-if="instance.erreur?.pile">
              <h2 class="h3">Détail technique de l’erreur</h2>
              <pre class="bloc-json">{{ instance.erreur.type }}&#10;{{ instance.erreur.pile }}</pre>
            </div>
          </div>
        </utd-onglet>
        <utd-onglet id="ongletHistorique" titre="Historique DurableTask">
          <div class="onglet-contenu">
            <table class="utd-table bordures-lignes compact">
              <caption class="utd-sr-only">Historique des événements DurableTask</caption>
              <thead>
                <tr>
                  <th scope="col">#</th>
                  <th scope="col">Horodatage</th>
                  <th scope="col">Événement</th>
                  <th scope="col">Nom</th>
                  <th scope="col">Données</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="h in historiqueFiltre" :key="`${h.executionId}-${h.sequence}`">
                  <td>{{ h.sequence }}</td>
                  <td>{{ heure(h.horodatage) }}</td>
                  <td class="historique-type">{{ libelleEvenement(h.type) }}</td>
                  <td>
                    <div v-if="h.nomResolu && NOMS_ACTIVITES[h.nomResolu]">{{ NOMS_ACTIVITES[h.nomResolu] }}</div>
                    <div class="texte-mono texte-attenue">{{ h.nomResolu ?? '' }}</div>
                  </td>
                  <td class="historique-donnees"><pre v-if="h.donnees">{{ json(h.donnees) }}</pre></td>
                </tr>
              </tbody>
            </table>
            <label class="interrupteur mt-8">
              <input v-model="historiqueComplet" type="checkbox" /> Afficher les événements techniques (OrchestratorStarted/Completed)
            </label>
          </div>
        </utd-onglet>
      </utd-onglets>

      <ModaleEvenement v-model:visible="modaleEvenement" :instance-id="instance.instanceId" :attendu="sp?.attente?.evenement" @envoye="actualiser" />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/lib/api'
import { dateHeure, duree, json, relatif, STATUTS_ACTIFS } from '@/lib/format'
import { depuisObjet, type ModeleProcessus } from '@/lib/modele'
import type { EvenementHistorique, InstanceDetail } from '@/lib/types'
import { useRafraichissement } from '@/lib/rafraichissement'
import PastilleStatut from '@/components/PastilleStatut.vue'
import BarreRafraichissement from '@/components/BarreRafraichissement.vue'
import GrapheProcessus from '@/components/graphe/GrapheProcessus.vue'
import ModaleEvenement from '@/components/ModaleEvenement.vue'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ id: string }>()
const router = useRouter()

const instance = ref<InstanceDetail | null>(null)
const historique = ref<EvenementHistorique[]>([])
const modele = ref<ModeleProcessus | null>(null)
const erreurChargement = ref('')
const modaleEvenement = ref(false)
const historiqueComplet = ref(false)

const sp = computed(() => instance.value?.statutPersonnalise)
const actif = computed(() => !!instance.value && STATUTS_ACTIFS.includes(instance.value.statut))

const { automatique, chargement, derniereMaj, actualiser } = useRafraichissement(async () => {
  try {
    const [i, h] = await Promise.all([api.instance(props.id), api.historique(props.id)])
    instance.value = i
    historique.value = h
    erreurChargement.value = ''
    if (!modele.value) {
      const def = await api.definition(i.processus, i.version ? Number(i.version) : undefined)
      modele.value = depuisObjet(def.definition)
    }
    // Une instance terminée n'évolue plus : inutile d'actualiser.
    if (!STATUTS_ACTIFS.includes(i.statut)) automatique.value = false
  } catch (e) {
    erreurChargement.value = (e as Error).message
  }
}, 3000)

// TaskCompleted/TaskFailed ne portent pas de nom : on le retrouve par le numéro de tâche
// (TaskID de l'événement de planification correspondant).
const historiqueFiltre = computed(() => {
  const noms = new Map<number, string>()
  for (const h of historique.value)
    if (h.tacheId !== undefined && h.tacheId !== null && h.nom && /Scheduled|Created/.test(h.type)) noms.set(h.tacheId, h.nom)
  return historique.value
    .filter((h) => historiqueComplet.value || !['OrchestratorStarted', 'OrchestratorCompleted'].includes(h.type))
    .map((h) => ({ ...h, nomResolu: h.nom ?? (h.tacheId !== undefined && h.tacheId !== null ? noms.get(h.tacheId) : undefined) }))
})

const NOMS_ACTIVITES: Record<string, string> = {
  'oim.chargerDefinition': 'Chargement de la définition',
  'oim.http': 'Appel HTTP',
  'oim.courriel': 'Courriel',
  'oim.reponse': 'Réponse à l’appelant'
}

const LIBELLES: Record<string, string> = {
  ExecutionStarted: '▶ Démarrage',
  ExecutionCompleted: '■ Fin',
  ExecutionTerminated: '■ Interruption',
  ExecutionSuspended: '⏸ Suspension',
  ExecutionResumed: '▶ Reprise',
  TaskScheduled: '→ Activité planifiée',
  TaskCompleted: '✓ Activité terminée',
  TaskFailed: '✗ Activité en échec',
  TimerCreated: '⏱ Minuterie créée',
  TimerFired: '⏱ Minuterie échue',
  EventRaised: '✉ Événement reçu',
  SubOrchestrationInstanceCreated: '⧉ Sous-processus créé',
  SubOrchestrationInstanceCompleted: '⧉ Sous-processus terminé',
  SubOrchestrationInstanceFailed: '⧉ Sous-processus en échec',
  OrchestratorStarted: 'Épisode démarré',
  OrchestratorCompleted: 'Épisode terminé'
}
const libelleEvenement = (t: string): string => LIBELLES[t] ?? t
const heure = (iso: string): string => new Date(iso).toLocaleString('fr-CA')

const commande = async (c: 'terminer' | 'suspendre' | 'reprendre' | 'relancer'): Promise<void> => {
  if (c === 'terminer') {
    const ok = await MessagesHelpers.confirmer(
      '<p>L’instance sera interrompue définitivement. Les étapes restantes ne seront pas exécutées.</p>',
      'Interrompre l’instance',
      'Interrompre'
    )
    if (!ok) return
  }
  try {
    await api.commande(props.id, c)
    MessagesHelpers.notifierSucces('Commande transmise.')
    automatique.value = true
    setTimeout(actualiser, 800)
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Commande refusée')
  }
}

const redemarrer = async (): Promise<void> => {
  try {
    const r = await api.redemarrer(props.id, false)
    MessagesHelpers.notifierSucces(`Nouvelle instance « ${r.instanceId} » démarrée.`)
    router.push(`/instances/${encodeURIComponent(r.instanceId)}`)
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Redémarrage impossible')
  }
}

const purger = async (): Promise<void> => {
  const ok = await MessagesHelpers.confirmer('<p>L’instance et tout son historique seront supprimés.</p>', 'Supprimer l’instance', 'Supprimer')
  if (!ok) return
  try {
    await api.purger(props.id)
    MessagesHelpers.notifierSucces('Instance supprimée.')
    router.push('/instances')
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`)
  }
}
</script>
