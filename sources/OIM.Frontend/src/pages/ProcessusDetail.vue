<template>
  <div>
    <utd-avis v-if="erreur" type="erreur" titre="Processus introuvable."><p>{{ erreur }}</p></utd-avis>

    <template v-if="detail && modele">
      <div class="entete-page">
        <div>
          <div class="entete-page-titre">
            <h1>{{ modele.nom ?? idLocal(detail.id) }}</h1>
            <span class="utd-etiquette">v{{ detail.version }}</span>
            <span v-if="resume" class="utd-pastille" :class="resume.actif ? 'vert' : 'gris'">{{ resume.actif ? 'Actif' : 'Désactivé' }}</span>
          </div>
          <p class="sous-titre texte-mono">{{ idLocal(detail.id) }}</p>
          <p v-if="modele.description" class="utd-largeur-max-zone-texte">{{ modele.description }}</p>
        </div>
        <div class="barre-actions">
          <button type="button" class="utd-btn primaire compact" :disabled="resume?.actif === false && versionAffichee === undefined" @click="modaleDemarrer = true">
            Démarrer une instance
          </button>
          <router-link :to="lienConcepteur(detail.id)" class="utd-btn secondaire compact">Ouvrir dans le concepteur</router-link>
          <button type="button" class="utd-btn secondaire compact" @click="api.telechargerZip(detail.id, detail.version)">
            Télécharger (.zip)
          </button>
          <button v-if="resume" type="button" class="utd-btn tertiaire comme-lien compact" @click="basculerActif">
            {{ resume.actif ? 'Désactiver' : 'Activer' }}
          </button>
        </div>
      </div>

      <utd-avis v-if="versionAffichee !== undefined && versionAffichee !== resume?.versionCourante" type="avertissement" titre="Version antérieure">
        <p>Vous consultez la version {{ versionAffichee }}. <router-link :to="lienProcessus(detail.id)">Afficher la version courante</router-link></p>
      </utd-avis>

      <dl class="grille-infos mb-16">
        <div><dt>Déployée le</dt><dd>{{ dateHeure(detail.deployeLe) }}</dd></div>
        <div><dt>Par</dt><dd>{{ detail.deployePar ?? '—' }}</dd></div>
        <div><dt>Entrées</dt><dd>{{ modele.entrees.map((e) => `${e.nom}${e.requis ? '*' : ''}: ${e.type}`).join(', ') || '—' }}</dd></div>
        <div><dt>Étapes</dt><dd>{{ modele.etapes.length }}</dd></div>
        <div><dt>Instances</dt><dd><router-link :to="{ path: lien('/instances'), query: { processus: detail.id } }">Voir les instances</router-link></dd></div>
      </dl>

      <utd-onglets id="ongletsProcessus" titre="Définition du processus">
        <utd-onglet id="ongletGraphe" titre="Graphe">
          <div class="onglet-contenu">
            <GrapheProcessus :modele="modele" />
          </div>
        </utd-onglet>
        <utd-onglet id="ongletYaml" titre="YAML">
          <div class="onglet-contenu">
            <pre class="bloc-json" style="max-height: 70vh">{{ detail.yaml }}</pre>
          </div>
        </utd-onglet>
        <utd-onglet id="ongletFichiers" :titre="`Fichiers annexes (${detail.fichiers.length})`">
          <div class="onglet-contenu">
            <p v-if="!detail.fichiers.length" class="zone-vide">Aucun fichier annexe.</p>
            <div v-for="f in detail.fichiers" :key="f.chemin" class="mb-16">
              <h2 class="h4 texte-mono">{{ f.chemin }}</h2>
              <pre class="bloc-json">{{ f.contenu }}</pre>
            </div>
          </div>
        </utd-onglet>
        <utd-onglet id="ongletTests" :titre="`Tests métier (${casTests.length})`">
          <div class="onglet-contenu">
            <p class="utd-largeur-max-zone-texte">
              Chaque fichier <code>tests/*.yml</code> décrit un cas : entrées, mocks des appels HTTP, scénario
              (événements, délais expirés) et résultat attendu. Les cas s’exécutent sur le vrai moteur, sans aucun
              appel ni courriel réel; ils sont aussi exécutés à chaque déploiement.
            </p>
            <div class="barre-actions mb-16">
              <button type="button" class="utd-btn primaire compact" :disabled="!casTests.length || testsEnCours" @click="executerTests">
                {{ testsEnCours ? 'Exécution…' : `Exécuter les tests (v${detail.version})` }}
              </button>
              <label class="interrupteur">
                <input v-model="conserverInstances" type="checkbox" /> Conserver les instances de test pour inspection
              </label>
            </div>
            <RapportTestsVue v-if="rapportTests" :rapport="rapportTests" />
            <ul v-else-if="casTests.length" class="liste-instances">
              <li v-for="c in casTests" :key="c" class="texte-mono" style="padding: 6px 4px">{{ c }}</li>
            </ul>
            <p v-else class="zone-vide">
              Aucun cas de test dans cette version. Ajoutez-en dans le
              <router-link :to="lienConcepteur(detail.id)">concepteur</router-link> (onglet « Fichiers annexes »).
            </p>
          </div>
        </utd-onglet>
        <utd-onglet id="ongletVersions" titre="Versions">
          <div class="onglet-contenu">
            <table class="utd-table bordures-lignes compact">
              <caption class="utd-sr-only">Historique des versions</caption>
              <thead>
                <tr>
                  <th scope="col">Version</th>
                  <th scope="col">Déployée le</th>
                  <th scope="col">Par</th>
                  <th scope="col">Commentaire</th>
                  <th scope="col">Empreinte</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="v in detail.versions" :key="v.version">
                  <td>
                    <router-link :to="{ path: lienProcessus(detail.id), query: { version: v.version } }">v{{ v.version }}</router-link>
                    <span v-if="v.version === resume?.versionCourante" class="utd-etiquette">courante</span>
                  </td>
                  <td>{{ dateHeure(v.deployeLe) }}</td>
                  <td>{{ v.deployePar }}</td>
                  <td>{{ v.commentaire }}</td>
                  <td class="texte-mono texte-attenue">{{ v.empreinte.slice(0, 12) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </utd-onglet>
      </utd-onglets>

      <ModaleDemarrer v-model:visible="modaleDemarrer" :processus="detail.id" :version="versionAffichee" :entrees="modele.entrees" />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '@/lib/api'
import { idLocal, useEquipe } from '@/lib/equipe'
import { dateHeure } from '@/lib/format'
import { depuisObjet, type ModeleProcessus } from '@/lib/modele'
import type { DefinitionDetail, RapportTests, ResumeDefinition } from '@/lib/types'
import GrapheProcessus from '@/components/graphe/GrapheProcessus.vue'
import ModaleDemarrer from '@/components/ModaleDemarrer.vue'
import RapportTestsVue from '@/components/RapportTests.vue'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ id: string }>()
const route = useRoute()
const { equipe, qualifier, lien, lienProcessus, lienConcepteur } = useEquipe()
/** Id complet (« equipe.processus ») : l'URL porte l'id du YAML. */
const idComplet = qualifier(props.id)

const detail = ref<DefinitionDetail | null>(null)
const resume = ref<ResumeDefinition | null>(null)
const modele = ref<ModeleProcessus | null>(null)
const erreur = ref('')
const modaleDemarrer = ref(false)

const casTests = computed(() => (detail.value?.fichiers ?? []).map((f) => f.chemin).filter((c) => /^tests\/.+\.ya?ml$/i.test(c)))
const rapportTests = ref<RapportTests | null>(null)
const testsEnCours = ref(false)
const conserverInstances = ref(false)

const executerTests = async (): Promise<void> => {
  if (!detail.value) return
  testsEnCours.value = true
  try {
    rapportTests.value = await api.tester(detail.value.id, { version: detail.value.version, conserver: conserverInstances.value })
    MessagesHelpers.notifierLecteurEcran(`${rapportTests.value.reussis} test(s) réussi(s) sur ${rapportTests.value.cas.length}.`)
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Exécution des tests impossible')
  } finally {
    testsEnCours.value = false
  }
}

const versionAffichee = computed(() => (route.query.version ? Number(route.query.version) : undefined))

const charger = async (): Promise<void> => {
  try {
    const [d, liste] = await Promise.all([api.definition(idComplet, versionAffichee.value), api.definitions(equipe.value)])
    detail.value = d
    resume.value = liste.find((x) => x.id === idComplet) ?? null
    modele.value = depuisObjet(d.definition)
  } catch (e) {
    erreur.value = (e as Error).message
  }
}

onMounted(charger)
watch(versionAffichee, charger)

const basculerActif = async (): Promise<void> => {
  if (!resume.value) return
  const actif = !resume.value.actif
  if (!actif) {
    const ok = await MessagesHelpers.confirmer(
      '<p>Aucune nouvelle instance ne pourra être démarrée. Les instances en cours se poursuivent normalement.</p>',
      'Désactiver le processus',
      'Désactiver'
    )
    if (!ok) return
  }
  await api.activer(idComplet, actif)
  MessagesHelpers.notifierSucces(actif ? 'Processus activé.' : 'Processus désactivé.')
  await charger()
}
</script>
