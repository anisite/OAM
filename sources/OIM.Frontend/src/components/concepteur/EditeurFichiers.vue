<template>
  <div class="fichiers-annexes">
    <div>
      <ul class="liste-fichiers" aria-label="Fichiers annexes">
        <li v-for="chemin in chemins" :key="chemin">
          <button type="button" :class="{ actif: chemin === selection }" @click="selection = chemin">{{ chemin }}</button>
        </li>
        <li v-if="!chemins.length"><p class="zone-vide">Aucun fichier.</p></li>
      </ul>
      <div class="barre-actions mt-8" style="flex-direction: column; align-items: stretch">
        <button type="button" class="utd-btn secondaire compact" @click="nouveau('requete')">+ Gabarit de requête HTTP</button>
        <button type="button" class="utd-btn secondaire compact" @click="nouveau('gabarit')">+ Gabarit de courriel</button>
        <button v-if="modele" type="button" class="utd-btn secondaire compact" @click="nouveauCas">+ Cas de test</button>
      </div>
    </div>

    <div v-if="selection && fichiers[selection] !== undefined">
      <div class="barre-actions mb-8">
        <span class="texte-mono"><strong>{{ selection }}</strong></span>
        <span style="flex: 1"></span>
        <button type="button" class="utd-btn tertiaire comme-lien compact" @click="renommer">Renommer</button>
        <button type="button" class="utd-btn avertissement compact" @click="supprimer">Supprimer</button>
      </div>
      <EditeurCode :model-value="fichiers[selection]" :libelle="`Contenu de ${selection}`" hauteur="520px" @update:model-value="fichiers[selection!] = $event" />
      <p class="texte-attenue mt-8">
        <template v-if="selection.startsWith('tests/')">
          Cas de test métier : <code>entrees</code>, <code>mocks</code> (par étape http), <code>scenario</code>
          (<code>attendre</code> + <code>evenement</code> ou <code>delaiExpire: true</code>) et <code>attendu</code> (comparaison partielle).
        </template>
        <template v-else-if="selection.startsWith('gabarits/')">
          Gabarit de courriel : <code>a</code>, <code>sujet</code>, <code>corps</code> (HTML), <code>format</code>. Syntaxe Handlebars sur le contexte :
          <code v-pre>{{entrees.x}}</code>, <code v-pre>{{#if evenement.motif}}…{{/if}}</code>.
        </template>
        <template v-else>
          Gabarit YamlHttpClient (<code>http_client</code>). Les « donnees » de l’étape sont le modèle Handlebars (<code v-pre>{{id}}</code>).
          Le bloc <code>mock</code> permet de simuler la réponse.
        </template>
      </p>
    </div>
    <p v-else class="zone-vide">Sélectionnez ou créez un fichier.</p>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import EditeurCode from './EditeurCode.vue'
import { ajouterFichier, casTestInitial } from '@/lib/gabarits'
import type { ModeleProcessus } from '@/lib/modele'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ fichiers: Record<string, string>; modele?: ModeleProcessus }>()

const nouveauCas = (): void => {
  const nom = window.prompt('Nom du cas de test (ex. approbation)')?.trim()
  if (!nom || !props.modele) return
  const chemin = `tests/${nom.replace(/\.ya?ml$/, '').replace(/[^\w-]+/g, '-')}.yml`
  if (!(chemin in props.fichiers)) props.fichiers[chemin] = casTestInitial(props.modele, nom)
  selection.value = chemin
}
const selection = ref<string | null>(Object.keys(props.fichiers)[0] ?? null)

const chemins = computed(() => Object.keys(props.fichiers).sort())

const nouveau = (type: 'requete' | 'gabarit'): void => {
  const nom = window.prompt(type === 'requete' ? 'Nom du gabarit de requête (ex. valider-dossier)' : 'Nom du gabarit de courriel (ex. confirmation)')
  if (!nom) return
  const chemin = ajouterFichier(props.fichiers, type, nom)
  selection.value = chemin
}

const renommer = (): void => {
  if (!selection.value) return
  const nouveauChemin = window.prompt('Nouveau chemin', selection.value)?.trim()
  if (!nouveauChemin || nouveauChemin === selection.value) return
  props.fichiers[nouveauChemin] = props.fichiers[selection.value]
  delete props.fichiers[selection.value]
  selection.value = nouveauChemin
}

const supprimer = async (): Promise<void> => {
  if (!selection.value) return
  if (!(await MessagesHelpers.confirmer(`<p>Supprimer « ${selection.value} » ?</p>`, 'Supprimer le fichier', 'Supprimer'))) return
  delete props.fichiers[selection.value]
  selection.value = chemins.value[0] ?? null
}

defineExpose({ selectionner: (c: string) => (selection.value = c) })
</script>

