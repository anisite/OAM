<template>
  <div>
    <utd-avis v-if="rapport.diagnostics.length" type="erreur" titre="La définition est invalide : les tests n’ont pas été exécutés.">
      <ul>
        <li v-for="(d, i) in rapport.diagnostics" :key="i"><strong v-if="d.etape">[{{ d.etape }}]</strong> {{ d.message }}</li>
      </ul>
    </utd-avis>

    <p v-else-if="!rapport.cas.length" class="zone-vide">
      Aucun cas de test. Ajoutez des fichiers <code>tests/*.yml</code> au paquet (onglet « Fichiers annexes » du concepteur).
    </p>

    <template v-else>
      <p class="barre-actions mb-8" role="status">
        <span class="utd-pastille" :class="rapport.reussi ? 'vert' : 'rouge'">
          {{ rapport.reussi ? '✓ Tous les tests réussissent' : `${rapport.echoues} test(s) en échec` }}
        </span>
        <span class="texte-attenue">{{ rapport.reussis }} / {{ rapport.cas.length }} réussi(s) · {{ dureeTotale }} s</span>
      </p>

      <table class="utd-table bordures-lignes compact">
        <caption class="utd-sr-only">Résultats des tests métier</caption>
        <thead>
          <tr>
            <th scope="col">Résultat</th>
            <th scope="col">Cas</th>
            <th scope="col">Écarts / obtenu</th>
            <th scope="col">Durée</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in rapport.cas" :key="c.fichier">
            <td><span class="utd-pastille" :class="c.reussi ? 'vert' : 'rouge'">{{ c.reussi ? 'Réussi' : 'Échec' }}</span></td>
            <td>
              <strong>{{ c.nom }}</strong>
              <div class="texte-attenue texte-mono">{{ c.fichier }}</div>
              <router-link v-if="c.instanceId" :to="`/instances/${encodeURIComponent(c.instanceId)}`">Voir l’instance de test</router-link>
            </td>
            <td>
              <ul v-if="c.ecarts.length" class="diagnostics">
                <li v-for="(e, i) in c.ecarts" :key="i">{{ e }}</li>
              </ul>
              <span v-else class="texte-attenue">
                {{ c.obtenu?.statut }}
                <template v-if="c.obtenu?.parcours?.length"> · {{ c.obtenu.parcours.join(' → ') }}</template>
              </span>
            </td>
            <td>{{ (c.dureeMs / 1000).toFixed(1) }} s</td>
          </tr>
        </tbody>
      </table>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { RapportTests } from '@/lib/types'

const props = defineProps<{ rapport: RapportTests }>()
const dureeTotale = computed(() => (Math.max(0, ...props.rapport.cas.map((c) => c.dureeMs)) / 1000).toFixed(1))
</script>
