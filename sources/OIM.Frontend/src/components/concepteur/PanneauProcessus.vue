<template>
  <div>
    <div class="panneau-entete">
      <h2>Paramètres du processus</h2>
      <p class="texte-attenue">Sélectionnez une étape pour la modifier.</p>
    </div>
    <div class="panneau-corps">
      <div class="champ-compact">
        <label for="ppId">Identifiant *</label>
        <input id="ppId" v-model="modele.id" class="texte-mono" />
        <span class="precision">Lettres, chiffres, tirets. Sert d’URL : /api/processus/{{ modele.id }}/instances</span>
      </div>
      <div class="champ-compact">
        <label for="ppNom">Nom</label>
        <input id="ppNom" v-model="modele.nom" />
      </div>
      <div class="champ-compact">
        <label for="ppDesc">Description</label>
        <textarea id="ppDesc" v-model="modele.description" rows="2"></textarea>
      </div>

      <p class="panneau-section-titre">Entrées</p>
      <p v-if="!modele.entrees.length" class="texte-attenue">Aucune entrée : le processus démarre sans données.</p>
      <div v-for="e in modele.entrees" :key="e.uid" class="bloc-branche sinon">
        <div class="bloc-entete">
          <input v-model="e.nom" class="texte-mono" aria-label="Nom de l’entrée" style="border: 1px solid #808a9d; border-radius: 4px; padding: 2px 6px" />
          <button type="button" class="btn-icone" :title="`Retirer ${e.nom}`" @click="retirer(e.uid)">
            <span aria-hidden="true">✕</span><span class="utd-sr-only">Retirer l’entrée {{ e.nom }}</span>
          </button>
        </div>
        <div class="ligne-kv" style="grid-template-columns: 1fr auto">
          <select v-model="e.type" :aria-label="`Type de ${e.nom}`">
            <option v-for="t in TYPES" :key="t" :value="t">{{ t }}</option>
          </select>
          <label class="interrupteur"><input v-model="e.requis" type="checkbox" /> requis</label>
        </div>
        <div class="champ-compact" style="margin-bottom: 0">
          <input v-model="e.libelle" placeholder="Libellé (formulaire de démarrage)" :aria-label="`Libellé de ${e.nom}`" />
        </div>
      </div>
      <button type="button" class="utd-btn tertiaire comme-lien compact" @click="ajouter">+ Ajouter une entrée</button>

      <p class="panneau-section-titre">Variables disponibles dans les expressions</p>
      <ul class="texte-attenue" style="padding-left: 18px">
        <li><code>entrees.&lt;nom&gt;</code></li>
        <li><code>etapes.&lt;id&gt;.sortie</code></li>
        <li><code>evenement</code> (dernier événement reçu)</li>
        <li><code>variables.&lt;nom&gt;</code>, <code>erreur.message</code></li>
        <li><code>instance.id</code>, <code>maintenant</code></li>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nouvelUid, type ModeleProcessus } from '@/lib/modele'

const props = defineProps<{ modele: ModeleProcessus }>()
const TYPES = ['string', 'int', 'number', 'bool', 'date', 'object', 'array']

const ajouter = (): void => {
  let n = 1
  while (props.modele.entrees.some((e) => e.nom === `entree${n}`)) n++
  props.modele.entrees.push({ uid: nouvelUid(), nom: `entree${n}`, type: 'string', requis: false })
}

const retirer = (uid: string): void => {
  props.modele.entrees = props.modele.entrees.filter((e) => e.uid !== uid)
}
</script>
