<template>
  <div>
    <div class="panneau-entete">
      <h2 id="titreProprietes">
        <span class="icone-etape" aria-hidden="true" :style="{ color: info.couleur }">{{ info.icone }}</span>
        {{ info.libelle }}
      </h2>
      <div class="barre-actions mt-8">
        <button type="button" class="utd-btn secondaire compact" @click="$emit('dupliquer')">Dupliquer</button>
        <button v-if="!estDepart" type="button" class="utd-btn secondaire compact" @click="$emit('depart')">Définir comme départ</button>
        <button type="button" class="utd-btn avertissement compact" @click="$emit('supprimer')">Supprimer</button>
      </div>
    </div>

    <div class="panneau-corps">
      <ul v-if="diagnostics.length" class="diagnostics mb-8">
        <li v-for="(d, i) in diagnostics" :key="i" :class="d.gravite">{{ d.message }}</li>
      </ul>

      <div class="champ-compact">
        <label for="prId">Identifiant</label>
        <input id="prId" :value="etape.id" class="texte-mono" @change="renommer(($event.target as HTMLInputElement).value)" />
        <span class="precision">Les références (suivant, siErreur…) sont mises à jour automatiquement.</span>
      </div>

      <div class="champ-compact">
        <label for="prType">Type</label>
        <select id="prType" v-model="etape.type">
          <option v-for="t in TYPES_ETAPES" :key="t.type" :value="t.type">{{ t.libelle }} ({{ t.type }})</option>
        </select>
      </div>

      <div class="champ-compact">
        <label for="prStatut">Statut métier</label>
        <input id="prStatut" v-model="etape.statut" placeholder="Ex. En attente d’approbation" />
        <span class="precision">Affiché au client et au tableau de bord pendant l’étape.</span>
      </div>

      <!-- Propriétés propres au type -->
      <p v-if="info.champs.length" class="panneau-section-titre">Paramètres</p>
      <template v-for="c in info.champs" :key="c.nom">
        <div v-if="c.type === 'objet'" class="champ-compact">
          <label>{{ c.libelle }}<span v-if="c.requis"> *</span></label>
          <EditeurObjet :model-value="etape.props[c.nom]" @update:model-value="etape.props[c.nom] = $event" />
          <span v-if="c.precision" class="precision">{{ c.precision }}</span>
        </div>

        <div v-else-if="c.type === 'json'" class="champ-compact">
          <label :for="`pr_${c.nom}`">{{ c.libelle }}<span v-if="c.requis"> *</span></label>
          <textarea
            :id="`pr_${c.nom}`"
            class="texte-mono champ-json"
            rows="8"
            :value="jsonTexte(etape.props[c.nom])"
            :aria-invalid="!!erreursJson[c.nom]"
            @change="majJson(c.nom, ($event.target as HTMLTextAreaElement).value)"
          ></textarea>
          <span v-if="erreursJson[c.nom]" class="erreur-json" role="alert">JSON invalide : {{ erreursJson[c.nom] }}</span>
          <span v-if="c.precision" class="precision">{{ c.precision }}</span>
        </div>

        <div v-else class="champ-compact">
          <label :for="`pr_${c.nom}`">{{ c.libelle }}<span v-if="c.requis"> *</span></label>
          <input
            :id="`pr_${c.nom}`"
            v-model="etape.props[c.nom]"
            class="texte-mono"
            :list="listeSuggestions(c.type) ? `liste_${c.nom}` : undefined"
            :placeholder="c.exemple"
          />
          <datalist v-if="listeSuggestions(c.type)" :id="`liste_${c.nom}`">
            <option v-for="s in listeSuggestions(c.type)" :key="s" :value="s"></option>
          </datalist>
          <span v-if="c.precision" class="precision">{{ c.precision }}</span>
          <button
            v-if="(c.type === 'requete' || c.type === 'gabarit') && etape.props[c.nom] && !fichierExiste(c.type, etape.props[c.nom])"
            type="button"
            class="utd-btn tertiaire comme-lien compact"
            @click="$emit('creerFichier', c.type, etape.props[c.nom])"
          >
            + Créer ce fichier
          </button>
        </div>
      </template>

      <div v-if="info.delaiExpire" class="champ-compact">
        <label for="prDelaiExpire">Si le délai expire, aller à</label>
        <select id="prDelaiExpire" v-model="etape.props.siDelaiExpire">
          <option :value="undefined">— (échec de l’étape)</option>
          <option v-for="id in autresEtapes" :key="id" :value="id">{{ id }}</option>
        </select>
      </div>

      <!-- Transitions -->
      <p class="panneau-section-titre">Suite</p>
      <label class="interrupteur mb-8">
        <input v-model="etape.fin" type="checkbox" /> Étape de fin
      </label>

      <template v-if="!etape.fin">
        <div v-for="(b, i) in conditionnelles" :key="`c${i}`" class="bloc-branche">
          <div class="bloc-entete">
            Condition {{ i + 1 }}
            <button type="button" class="btn-icone" title="Retirer la condition" @click="retirerBranche(b)">
              <span aria-hidden="true">✕</span><span class="utd-sr-only">Retirer la condition {{ i + 1 }}</span>
            </button>
          </div>
          <div class="champ-compact">
            <label :for="`si${i}`">Si</label>
            <input :id="`si${i}`" v-model="b.si" class="texte-mono" placeholder="{{ evenement.approuve == true }}" />
          </div>
          <div class="champ-compact">
            <label :for="`aller${i}`">Aller à</label>
            <select :id="`aller${i}`" v-model="b.aller">
              <option value="">—</option>
              <option v-for="id in autresEtapes" :key="id" :value="id">{{ id }}</option>
            </select>
          </div>
        </div>

        <div class="bloc-branche" :class="{ sinon: conditionnelles.length > 0 }">
          <div class="champ-compact" style="margin-bottom: 0">
            <label for="prSuivant">{{ conditionnelles.length ? 'Sinon, aller à' : 'Étape suivante' }}</label>
            <select id="prSuivant" :value="sinon?.aller ?? ''" @change="definirSinon(($event.target as HTMLSelectElement).value)">
              <option value="">— (fin du processus)</option>
              <option v-for="id in autresEtapes" :key="id" :value="id">{{ id }}</option>
            </select>
          </div>
        </div>
        <button type="button" class="utd-btn tertiaire comme-lien compact" @click="ajouterCondition">+ Ajouter une condition</button>
      </template>

      <div class="champ-compact mt-16">
        <label for="prSiErreur">En cas d’erreur, aller à</label>
        <select id="prSiErreur" v-model="etape.siErreur">
          <option :value="undefined">— (l’instance passe en échec)</option>
          <option v-for="id in autresEtapes" :key="id" :value="id">{{ id }}</option>
        </select>
        <span class="precision">Le message d’erreur est disponible via erreur.message.</span>
      </div>

      <template v-if="info.retry">
        <p class="panneau-section-titre">Reprises automatiques</p>
        <label class="interrupteur mb-8">
          <input :checked="!!etape.retry" type="checkbox" @change="basculerRetry(($event.target as HTMLInputElement).checked)" /> Réessayer en cas d’échec
        </label>
        <div v-if="etape.retry" class="ligne-kv" style="grid-template-columns: 1fr 1fr 1fr">
          <div class="champ-compact"><label for="rtT">Tentatives</label><input id="rtT" v-model.number="etape.retry.tentatives" type="number" min="1" /></div>
          <div class="champ-compact"><label for="rtD">Délai</label><input id="rtD" v-model="etape.retry.delai" class="texte-mono" /></div>
          <div class="champ-compact"><label for="rtB">Backoff</label><input id="rtB" v-model.number="etape.retry.backoff" type="number" min="1" step="0.5" /></div>
        </div>
      </template>

      <p class="panneau-section-titre">Retour au client</p>
      <div class="champ-compact">
        <label for="prMessage">Message</label>
        <input id="prMessage" v-model="etape.message" class="texte-mono" placeholder="Confirmation #{{ etapes.valider.sortie.numero }}" />
        <span class="precision">Évalué après l’étape; retourné dans le statut et la sortie de l’instance.</span>
      </div>

      <div class="champ-compact">
        <label for="prDescription">Description</label>
        <textarea id="prDescription" v-model="etape.description" rows="2"></textarea>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import EditeurObjet from './EditeurObjet.vue'
import { TYPES_ETAPES, typeEtape, type TypeChamp } from '@/lib/catalogue'
import type { BrancheModele, EtapeModele, ModeleProcessus } from '@/lib/modele'
import type { Diagnostic } from '@/lib/types'

const props = defineProps<{
  etape: EtapeModele
  modele: ModeleProcessus
  fichiers: Record<string, string>
  processus: string[]
  diagnostics: Diagnostic[]
}>()
const emit = defineEmits<{
  (e: 'renommer', ancien: string, nouveau: string): void
  (e: 'supprimer'): void
  (e: 'dupliquer'): void
  (e: 'depart'): void
  (e: 'creerFichier', type: TypeChamp, nom: string): void
}>()

const info = computed(() => typeEtape(props.etape.type))

// Champs « json » : la valeur n'est remplacée que si le texte est du JSON valide.
const erreursJson = reactive<Record<string, string>>({})
const jsonTexte = (v: unknown): string => (v === undefined || v === null ? '' : JSON.stringify(v, null, 2))
const majJson = (nom: string, texte: string): void => {
  if (!texte.trim()) {
    delete props.etape.props[nom]
    delete erreursJson[nom]
    return
  }
  try {
    props.etape.props[nom] = JSON.parse(texte)
    delete erreursJson[nom]
  } catch (e) {
    erreursJson[nom] = (e as Error).message
  }
}
const estDepart = computed(() => props.modele.etapes[0]?.uid === props.etape.uid)
const autresEtapes = computed(() => props.modele.etapes.map((e) => e.id))
const conditionnelles = computed(() => props.etape.suivant.filter((b) => b.si !== null))
const sinon = computed(() => props.etape.suivant.find((b) => b.si === null))

const requetes = computed(() =>
  Object.entries(props.fichiers)
    .filter(([, c]) => /^http_client\s*:/m.test(c))
    .map(([chemin]) => chemin)
)
const gabarits = computed(() =>
  Object.keys(props.fichiers)
    .filter((c) => c.startsWith('gabarits/'))
    .map((c) => c.replace(/^gabarits\//, '').replace(/\.ya?ml$/, ''))
)

const listeSuggestions = (t: TypeChamp): string[] | null =>
  t === 'requete' ? requetes.value : t === 'gabarit' ? gabarits.value : t === 'processus' ? props.processus : null

const fichierExiste = (t: TypeChamp, nom: string): boolean =>
  t === 'requete' ? Object.keys(props.fichiers).some((f) => f === nom.split('#')[0] || f === `requetes/${nom.split('#')[0]}`) : gabarits.value.includes(nom)

const renommer = (nouveau: string): void => {
  nouveau = nouveau.trim()
  if (nouveau && nouveau !== props.etape.id) emit('renommer', props.etape.id, nouveau)
}

const ajouterCondition = (): void => {
  const b: BrancheModele = { si: '{{ }}', aller: '' }
  const i = props.etape.suivant.findIndex((x) => x.si === null)
  if (i >= 0) props.etape.suivant.splice(i, 0, b)
  else props.etape.suivant.push(b)
}

const retirerBranche = (b: BrancheModele): void => {
  props.etape.suivant.splice(props.etape.suivant.indexOf(b), 1)
}

const definirSinon = (aller: string): void => {
  const i = props.etape.suivant.findIndex((b) => b.si === null)
  if (!aller) {
    if (i >= 0) props.etape.suivant.splice(i, 1)
  } else if (i >= 0) props.etape.suivant[i].aller = aller
  else props.etape.suivant.push({ si: null, aller })
}

const basculerRetry = (actif: boolean): void => {
  props.etape.retry = actif ? { tentatives: 3, delai: '00:00:30', backoff: 2 } : null
}
</script>

<style scoped>
.champ-json {
  width: 100%;
  font-size: 0.8125rem;
  resize: vertical;
}
.erreur-json {
  color: var(--oim-rouge);
  font-size: 0.8125rem;
}
</style>
