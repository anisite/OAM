<template>
  <div>
    <div class="entete-page">
      <div>
        <h1>Concepteur de processus</h1>
        <p class="sous-titre">
          <template v-if="idCharge">Modification de <span class="texte-mono">{{ idLocal(idCharge) }}</span> (déployée : v{{ versionChargee }})</template>
          <template v-else>Nouveau processus</template>
        </p>
      </div>
      <div class="barre-actions">
        <button type="button" class="utd-btn tertiaire comme-lien compact" @click="nouveau">Nouveau</button>
        <button v-if="!modele.etapes.length" type="button" class="utd-btn secondaire compact" @click="chargerExemple">Charger l’exemple</button>
        <input ref="champFichier" type="file" accept=".yml,.yaml" class="utd-d-none" aria-label="Fichier YAML à ouvrir" @change="ouvrirFichier" />
        <button type="button" class="utd-btn secondaire compact" @click="champFichier?.click()">Ouvrir un YAML</button>
        <button type="button" class="utd-btn secondaire compact" @click="telecharger">Télécharger le YAML</button>
        <button type="button" class="utd-btn primaire compact" :disabled="deploiement || nbErreurs > 0" @click="deployer">
          Déployer
        </button>
      </div>
    </div>

    <!-- État de validation (serveur, en continu) -->
    <div class="concepteur-barre" role="status">
      <span v-if="validation === null" class="texte-attenue">Validation…</span>
      <span v-else-if="nbErreurs === 0" class="utd-pastille vert">✓ Définition valide</span>
      <span v-else class="utd-pastille rouge">{{ nbErreurs }} erreur(s)</span>
      <span v-if="nbAvertissements" class="utd-pastille jaune">{{ nbAvertissements }} avertissement(s)</span>
      <span class="texte-attenue">{{ modele.etapes.length }} étape(s) · {{ Object.keys(fichiers).length }} fichier(s) annexe(s)</span>
      <span class="concepteur-barre-espace"></span>
      <button type="button" class="utd-btn secondaire compact" :disabled="!nbCasTests || testsEnCours || nbErreurs > 0" @click="executerTests">
        {{ testsEnCours ? 'Tests en cours…' : `Exécuter les tests (${nbCasTests})` }}
      </button>
      <button type="button" class="utd-btn tertiaire comme-lien compact" @click="reorganiser">Réorganiser le canevas</button>
    </div>
    <ul v-if="diagnosticsGeneraux.length" class="diagnostics mb-8">
      <li v-for="(d, i) in diagnosticsGeneraux" :key="i" :class="d.gravite">
        <button v-if="d.etape" type="button" @click="selectionnerParId(d.etape)">{{ d.etape }}</button>
        {{ d.message }}
      </li>
    </ul>

    <utd-onglets id="ongletsConcepteur" titre="Édition du processus">
      <utd-onglet id="ongletCanevas" titre="Canevas">
        <div class="onglet-contenu">
          <div class="concepteur-zone">
            <PaletteEtapes @ajouter="ajouterAuCentre" />

            <div ref="conteneur" class="concepteur-canevas" @dragover.prevent="surSurvol" @drop.prevent="surDepot">
              <VueFlow
                :id="ID_FLUX"
                v-model:nodes="noeuds"
                :edges="liens"
                :default-viewport="{ x: 40, y: 20, zoom: 0.9 }"
                :min-zoom="0.2"
                :max-zoom="1.6"
                :delete-key-code="['Delete', 'Backspace']"
                :connection-radius="30"
                @connect="relier"
                @edges-change="surChangementLiens"
                @nodes-change="surChangementNoeuds"
                @node-click="(e) => selectionner(e.node.id)"
                @pane-click="selectionner(null)"
              >
                <template #node-etape="p">
                  <NoeudEtape :id="p.id" :data="p.data" :selected="p.id === selection" />
                </template>
                <Background :gap="24" :size="1.2" pattern-color="#cfd8e3" />
                <Controls />
                <MiniMap :node-color="couleurMiniCarte" pannable zoomable :width="140" :height="90" />
              </VueFlow>
              <p v-if="!modele.etapes.length" class="zone-vide" style="position: absolute; top: 40%; left: 0; right: 0; pointer-events: none">
                Glissez une étape de la palette ici pour commencer.
              </p>
            </div>

            <aside class="concepteur-proprietes" aria-labelledby="titreProprietes">
              <PanneauEtape
                v-if="etapeSelectionnee"
                :key="etapeSelectionnee.uid"
                :etape="etapeSelectionnee"
                :modele="modele"
                :fichiers="fichiers"
                :processus="processusDisponibles"
                :diagnostics="diagnosticsEtape(etapeSelectionnee.id)"
                @renommer="(a: string, n: string) => renommer(a, n)"
                @supprimer="supprimer(etapeSelectionnee.uid)"
                @dupliquer="dupliquer(etapeSelectionnee)"
                @depart="definirDepart(etapeSelectionnee.uid)"
                @creer-fichier="creerFichier"
              />
              <PanneauProcessus v-else :modele="modele" />
            </aside>
          </div>
        </div>
      </utd-onglet>

      <utd-onglet id="ongletYamlConcepteur" titre="YAML">
        <div class="onglet-contenu">
          <utd-avis v-if="yamlModifie" type="avertissement" titre="Le YAML a été modifié.">
            <p>Appliquez vos modifications pour mettre le canevas à jour.</p>
            <div class="barre-actions">
              <button type="button" class="utd-btn primaire compact" @click="appliquerYaml">Appliquer au canevas</button>
              <button type="button" class="utd-btn tertiaire comme-lien compact" @click="annulerYaml">Annuler les modifications</button>
            </div>
          </utd-avis>
          <utd-avis v-if="erreurYaml" type="erreur" titre="YAML invalide"><p>{{ erreurYaml }}</p></utd-avis>
          <EditeurCode v-model="brouillonYaml" libelle="Définition YAML du processus" hauteur="600px" />
        </div>
      </utd-onglet>

      <utd-onglet id="ongletFichiersConcepteur" titre="Fichiers annexes">
        <div class="onglet-contenu">
          <p class="texte-attenue">Gabarits de requêtes HTTP (YamlHttpClient) et de courriels, déployés avec le processus.</p>
          <EditeurFichiers ref="editeurFichiers" :fichiers="fichiers" :modele="modele" />
        </div>
      </utd-onglet>
    </utd-onglets>

    <Dialogue id="dialogueTests" titre="Tests métier" :visible="!!rapportTests" @update:visible="(v: boolean) => { if (!v) rapportTests = null }">
      <utd-avis v-if="refusParTests" type="erreur" titre="Déploiement refusé : des tests métier échouent.">
        <p>Corrigez le processus ou les cas de test. Vous pouvez aussi déployer malgré les échecs.</p>
      </utd-avis>
      <RapportTestsVue v-if="rapportTests" :rapport="rapportTests" />
      <template #pied>
        <button type="button" class="utd-btn secondaire compact" @click="rapportTests = null">Fermer</button>
        <button v-if="refusParTests" type="button" class="utd-btn avertissement compact" :disabled="deploiement" @click="deployerMalgreTests">
          Déployer quand même
        </button>
      </template>
    </Dialogue>

    <Dialogue id="dialogueDeployer" titre="Déployer le processus" :visible="modaleDeployer" @update:visible="modaleDeployer = $event">
      <p>
        Une nouvelle version de <strong class="texte-mono">{{ modele.id }}</strong> sera créée si le contenu a changé.
        Les instances en cours conservent leur version.
      </p>
      <utd-champ-form id="champCommentaire" libelle="Commentaire de version" format="xl">
        <input v-model="commentaire" type="text" placeholder="Ex. Ajout de la relance" />
      </utd-champ-form>
      <template #pied>
        <button type="button" class="utd-btn secondaire compact" @click="modaleDeployer = false">Annuler</button>
        <button type="button" class="utd-btn primaire compact" :disabled="deploiement" @click="confirmerDeploiement()">
          {{ deploiement && nbCasTests ? 'Tests et déploiement…' : 'Déployer' }}
        </button>
      </template>
    </Dialogue>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { VueFlow, useVueFlow, type Connection, type EdgeChange, type Node, type NodeChange } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import NoeudEtape, { type DonneesNoeud } from '@/components/graphe/NoeudEtape.vue'
import PaletteEtapes from '@/components/concepteur/PaletteEtapes.vue'
import PanneauEtape from '@/components/concepteur/PanneauEtape.vue'
import PanneauProcessus from '@/components/concepteur/PanneauProcessus.vue'
import EditeurCode from '@/components/concepteur/EditeurCode.vue'
import EditeurFichiers from '@/components/concepteur/EditeurFichiers.vue'
import Dialogue from '@/components/Dialogue.vue'
import RapportTestsVue from '@/components/RapportTests.vue'
import { api, ErreurApi } from '@/lib/api'
import { idLocal, useEquipe } from '@/lib/equipe'
import { typeEtape, type TypeChamp } from '@/lib/catalogue'
import { aretes, disposer, type Poignee } from '@/lib/graphe'
import {
  depuisYaml,
  modeleVide,
  nouvelleEtape,
  idUnique,
  nouvelUid,
  renommerEtape,
  supprimerEtape,
  versYaml,
  type EtapeModele,
  type ModeleProcessus
} from '@/lib/modele'
import { ajouterFichier, EXEMPLE_YAML, gabaritFichier } from '@/lib/gabarits'
import type { Diagnostic, RapportTests, ReponseValidation } from '@/lib/types'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ id?: string }>()
const router = useRouter()
// Le processus est chargé et déployé dans l'équipe de l'URL; props.id est l'id du YAML.
const { equipe, qualifier, lienConcepteur } = useEquipe()

const ID_FLUX = 'concepteur'
const { screenToFlowCoordinate, fitView } = useVueFlow(ID_FLUX)

// ── État ─────────────────────────────────────────────────────────────────────
const modele = reactive<ModeleProcessus>(modeleVide())
const fichiers = reactive<Record<string, string>>({})
const noeuds = ref<Node<DonneesNoeud>[]>([])
const selection = ref<string | null>(null)
const idCharge = ref<string | null>(null)
const versionChargee = ref<number | null>(null)
const processusDisponibles = ref<string[]>([])

const conteneur = ref<HTMLDivElement | null>(null)
const champFichier = ref<HTMLInputElement | null>(null)
const editeurFichiers = ref<InstanceType<typeof EditeurFichiers> | null>(null)

const validation = ref<ReponseValidation | null>(null)

const etapeSelectionnee = computed(() => modele.etapes.find((e) => e.uid === selection.value) ?? null)

// ── Chargement ───────────────────────────────────────────────────────────────
const remplacer = (m: ModeleProcessus, f: Record<string, string>): void => {
  Object.assign(modele, m)
  for (const k of Object.keys(fichiers)) delete fichiers[k]
  Object.assign(fichiers, f)
  selection.value = null
  const positions = disposer(modele)
  noeuds.value = modele.etapes.map((e) => creerNoeud(e, positions[e.uid]))
  nextTick(() => setTimeout(() => fitView({ padding: 0.15, maxZoom: 1 }), 60))
}

onMounted(async () => {
  // Sous-processus : toujours dans la même équipe, désignés par l'id du YAML.
  api.definitions(equipe.value).then((l) => (processusDisponibles.value = l.map((d) => idLocal(d.id)))).catch(() => undefined)
  if (props.id) {
    try {
      const d = await api.definition(qualifier(props.id))
      idCharge.value = d.id
      versionChargee.value = d.version
      remplacer(depuisYaml(d.yaml), Object.fromEntries(d.fichiers.map((f) => [f.chemin, f.contenu])))
    } catch (e) {
      MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Chargement impossible')
    }
  } else remplacer(modeleVide(), {})
})

const nouveau = async (): Promise<void> => {
  if (modele.etapes.length && !(await MessagesHelpers.confirmer('<p>Les modifications non déployées seront perdues.</p>', 'Nouveau processus', 'Continuer'))) return
  idCharge.value = null
  router.replace(lienConcepteur())
  remplacer(modeleVide(), {})
}

const chargerExemple = (): void => {
  const f: Record<string, string> = {}
  ajouterFichier(f, 'requete', 'valider-dossier')
  f['valider-dossier.yml'] = gabaritFichier('requete', 'valider-dossier').replace('{ "ok": true }', '{ "valide": true, "numero": "D-2026-00042" }')
  for (const g of ['relance-approbateur', 'confirmation', 'refus']) ajouterFichier(f, 'gabarit', g)
  f['gabarits/relance-approbateur.yml'] = f['gabarits/relance-approbateur.yml'].replace('# a: destinataire', 'a: approbateurs@exemple.gouv.qc.ca #')
  remplacer(depuisYaml(EXEMPLE_YAML.replace('id: traitement-demande', 'id: traitement-demande-copie')), f)
}

// ── Noeuds et liens ──────────────────────────────────────────────────────────
const creerNoeud = (e: EtapeModele, position = { x: 80, y: 80 }): Node<DonneesNoeud> => ({
  id: e.uid,
  type: 'etape',
  position,
  data: { etape: e, mode: 'edition', onSupprimer: (uid: string) => supprimer(uid) }
})

// Les données dérivées (départ, erreurs) sont recalculées sans recréer les noeuds.
watch(
  () => [modele.etapes.map((e) => e.uid).join(), validation.value],
  () => {
    for (const n of noeuds.value) {
      const e = modele.etapes.find((x) => x.uid === n.id)
      if (!e) continue
      n.data = { ...n.data, mode: 'edition', etape: e, depart: modele.etapes[0]?.uid === n.id, nbErreurs: diagnosticsEtape(e.id).filter((d) => d.gravite === 'erreur').length }
    }
  },
  { deep: false }
)

const liens = computed(() => aretes(modele))

const couleurMiniCarte = (n: Node<DonneesNoeud>): string => typeEtape(n.data?.etape.type ?? '').couleur

const ajouterEtape = (type: string, position: { x: number; y: number }): void => {
  const e = nouvelleEtape(modele, type)
  modele.etapes.push(e)
  // Commodité : relier automatiquement depuis l'étape sélectionnée si elle n'a pas de suite.
  const precedente = etapeSelectionnee.value
  if (precedente && !precedente.fin && precedente.suivant.length === 0) precedente.suivant.push({ si: null, aller: e.id })
  noeuds.value = [...noeuds.value, creerNoeud(modele.etapes[modele.etapes.length - 1], position)]
  selection.value = e.uid
}

const surSurvol = (ev: DragEvent): void => {
  if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'move'
}

const surDepot = (ev: DragEvent): void => {
  const type = ev.dataTransfer?.getData('application/oim-etape')
  if (!type) return
  const p = screenToFlowCoordinate({ x: ev.clientX, y: ev.clientY })
  // Même point de saisie que l'aperçu de glisser (PaletteEtapes) : le noeud tombe là où on le voyait.
  ajouterEtape(type, { x: p.x - 115, y: p.y - 14 })
}

const ajouterAuCentre = (type: string): void => {
  const r = conteneur.value?.getBoundingClientRect()
  const p = r ? screenToFlowCoordinate({ x: r.left + r.width / 2, y: r.top + r.height / 3 }) : { x: 100, y: 100 }
  ajouterEtape(type, { x: p.x - 115 + Math.random() * 40, y: p.y })
}

/** Nouveau lien tracé à la souris : on met à jour le modèle, les liens en découlent. */
const relier = (c: Connection): void => {
  const source = modele.etapes.find((e) => e.uid === c.source)
  const cible = modele.etapes.find((e) => e.uid === c.target)
  if (!source || !cible) return
  const poignee = (c.sourceHandle ?? 'suivant') as Poignee

  if (poignee === 'erreur') source.siErreur = cible.id
  else if (poignee === 'delai') source.props.siDelaiExpire = cible.id
  else if (poignee.startsWith('b')) {
    const i = Number(poignee.slice(1))
    const b = source.suivant.filter((x) => x.si !== null)[i]
    if (b) b.aller = cible.id
  } else {
    source.fin = false
    const sinon = source.suivant.find((b) => b.si === null)
    if (sinon) sinon.aller = cible.id
    else source.suivant.push({ si: null, aller: cible.id })
  }
}

const surChangementLiens = (changements: EdgeChange[]): void => {
  for (const ch of changements) {
    if (ch.type !== 'remove') continue
    const [uid, poignee] = ch.id.split('|')
    const e = modele.etapes.find((x) => x.uid === uid)
    if (!e) continue
    if (poignee === 'erreur') e.siErreur = undefined
    else if (poignee === 'delai') delete e.props.siDelaiExpire
    else if (poignee.startsWith('b')) {
      const b = e.suivant.filter((x) => x.si !== null)[Number(poignee.slice(1))]
      if (b) b.aller = ''
    } else e.suivant = e.suivant.filter((b) => b.si !== null)
  }
}

const surChangementNoeuds = (changements: NodeChange[]): void => {
  for (const ch of changements)
    if (ch.type === 'remove') {
      supprimerEtape(modele, ch.id)
      if (selection.value === ch.id) selection.value = null
    }
}

const selectionner = (uid: string | null): void => {
  selection.value = uid
}

const selectionnerParId = (id: string): void => {
  const e = modele.etapes.find((x) => x.id === id)
  if (e) {
    selection.value = e.uid
    document.getElementById('ongletsConcepteur')?.setAttribute('id-onglet-actif', 'ongletCanevas')
  }
}

// ── Opérations sur les étapes ────────────────────────────────────────────────
const renommer = (ancien: string, nouveau: string): void => {
  if (modele.etapes.some((e) => e.id === nouveau)) {
    MessagesHelpers.notifierErreur(`L’identifiant « ${nouveau} » est déjà utilisé.`)
    return
  }
  renommerEtape(modele, ancien, nouveau)
}

const supprimer = async (uid: string): Promise<void> => {
  const e = modele.etapes.find((x) => x.uid === uid)
  if (!e) return
  if (!(await MessagesHelpers.confirmer(`<p>Supprimer l’étape « ${e.id} » et les liens qui y mènent ?</p>`, 'Supprimer l’étape', 'Supprimer'))) return
  supprimerEtape(modele, uid)
  noeuds.value = noeuds.value.filter((n) => n.id !== uid)
  if (selection.value === uid) selection.value = null
}

const dupliquer = (e: EtapeModele): void => {
  const copie: EtapeModele = { ...JSON.parse(JSON.stringify(e)), uid: nouvelUid(), id: idUnique(modele, `${e.id}Copie`) }
  modele.etapes.push(copie)
  const n = noeuds.value.find((x) => x.id === e.uid)
  noeuds.value = [...noeuds.value, creerNoeud(modele.etapes[modele.etapes.length - 1], { x: (n?.position.x ?? 0) + 40, y: (n?.position.y ?? 0) + 40 })]
  selection.value = copie.uid
}

const definirDepart = (uid: string): void => {
  const i = modele.etapes.findIndex((e) => e.uid === uid)
  if (i > 0) modele.etapes.unshift(...modele.etapes.splice(i, 1))
}

const creerFichier = (type: TypeChamp, nom: string): void => {
  const chemin = ajouterFichier(fichiers, type === 'requete' ? 'requete' : 'gabarit', nom)
  MessagesHelpers.notifierSucces(`Fichier « ${chemin} » créé : complétez-le dans l’onglet « Fichiers annexes ».`)
  editeurFichiers.value?.selectionner(chemin)
}

const reorganiser = (): void => {
  const positions = disposer(modele)
  noeuds.value = noeuds.value.map((n) => ({ ...n, position: positions[n.id] ?? n.position }))
  nextTick(() => fitView({ padding: 0.15, maxZoom: 1 }))
}

// ── YAML ─────────────────────────────────────────────────────────────────────
const yamlCourant = computed(() => versYaml(modele))
const brouillonYaml = ref('')
const erreurYaml = ref('')
const yamlModifie = computed(() => brouillonYaml.value !== yamlCourant.value)

watch(yamlCourant, (v, ancien) => {
  // On ne remplace pas un brouillon en cours d'édition.
  if (brouillonYaml.value === ancien || !brouillonYaml.value) brouillonYaml.value = v
}, { immediate: true })

const appliquerYaml = (): void => {
  try {
    remplacer(depuisYaml(brouillonYaml.value), { ...fichiers })
    brouillonYaml.value = yamlCourant.value
    erreurYaml.value = ''
    MessagesHelpers.notifierSucces('Le canevas a été mis à jour à partir du YAML.')
  } catch (e) {
    erreurYaml.value = (e as Error).message
  }
}

const annulerYaml = (): void => {
  brouillonYaml.value = yamlCourant.value
  erreurYaml.value = ''
}

const ouvrirFichier = async (ev: Event): Promise<void> => {
  const input = ev.target as HTMLInputElement
  const f = input.files?.[0]
  input.value = ''
  if (!f) return
  try {
    remplacer(depuisYaml(await f.text()), { ...fichiers })
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'YAML illisible')
  }
}

const telecharger = (): void => {
  const url = URL.createObjectURL(new Blob([yamlCourant.value], { type: 'application/yaml' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `${modele.id || 'processus'}.yml`
  a.click()
  URL.revokeObjectURL(url)
}

// ── Validation continue (serveur) ────────────────────────────────────────────
let minuterieValidation: number | undefined

watch(
  () => [yamlCourant.value, JSON.stringify(fichiers)],
  () => {
    window.clearTimeout(minuterieValidation)
    minuterieValidation = window.setTimeout(async () => {
      try {
        validation.value = await api.valider(yamlCourant.value, { ...fichiers })
      } catch {
        /* serveur indisponible : on garde le dernier résultat */
      }
    }, 500)
  },
  { immediate: true }
)

const diagnostics = computed<Diagnostic[]>(() => validation.value?.diagnostics ?? [])
const nbErreurs = computed(() => diagnostics.value.filter((d) => d.gravite === 'erreur').length)
const nbAvertissements = computed(() => diagnostics.value.filter((d) => d.gravite === 'avertissement').length)
const diagnosticsEtape = (id: string): Diagnostic[] => diagnostics.value.filter((d) => d.etape === id)
const diagnosticsGeneraux = computed(() => diagnostics.value.filter((d) => d.gravite === 'erreur' || !d.etape).slice(0, 8))

// ── Déploiement ──────────────────────────────────────────────────────────────
const modaleDeployer = ref(false)
const deploiement = ref(false)
const commentaire = ref('')

const deployer = (): void => {
  commentaire.value = ''
  modaleDeployer.value = true
}

// ── Tests métier ─────────────────────────────────────────────────────────────
const nbCasTests = computed(() => Object.keys(fichiers).filter((c) => /^tests\/.+\.ya?ml$/i.test(c)).length)
const rapportTests = ref<RapportTests | null>(null)
const refusParTests = ref(false)
const testsEnCours = ref(false)

const executerTests = async (): Promise<void> => {
  testsEnCours.value = true
  try {
    refusParTests.value = false
    rapportTests.value = await api.testerPaquet(equipe.value, yamlCourant.value, { ...fichiers })
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Exécution des tests impossible')
  } finally {
    testsEnCours.value = false
  }
}

const deployerMalgreTests = async (): Promise<void> => {
  if (await MessagesHelpers.confirmer('<p>La version sera déployée même si des tests métier échouent.</p>', 'Déployer quand même', 'Déployer'))
    await confirmerDeploiement(true)
}

const confirmerDeploiement = async (ignorerTests = false): Promise<void> => {
  deploiement.value = true
  try {
    const r = await api.deployer(equipe.value, yamlCourant.value, { ...fichiers }, commentaire.value || undefined, ignorerTests)
    modaleDeployer.value = false
    rapportTests.value = null
    refusParTests.value = false
    idCharge.value = r.id
    versionChargee.value = r.version
    const tests = r.tests ? ` Tests métier : ${r.tests.reussis}/${r.tests.cas.length} réussi(s).` : ''
    MessagesHelpers.notifierSucces(
      (r.nouvelleVersion ? `« ${idLocal(r.id)} » v${r.version} est déployé et prêt à recevoir des instances.` : `Aucun changement : « ${idLocal(r.id)} » reste en v${r.version}.`) + tests,
      'Déploiement'
    )
    router.replace(lienConcepteur(r.id))
  } catch (e) {
    if (e instanceof ErreurApi && e.statut === 422 && e.corps?.tests) {
      modaleDeployer.value = false
      refusParTests.value = true
      rapportTests.value = e.corps.tests
      return
    }
    const detail = e instanceof ErreurApi && e.corps?.diagnostics
      ? `<ul>${e.corps.diagnostics.filter((d: Diagnostic) => d.gravite === 'erreur').map((d: Diagnostic) => `<li>${d.etape ? `[${d.etape}] ` : ''}${d.message}</li>`).join('')}</ul>`
      : `<p>${(e as Error).message}</p>`
    MessagesHelpers.afficherErreurTechnique(detail, 'Déploiement refusé')
  } finally {
    deploiement.value = false
  }
}
</script>
