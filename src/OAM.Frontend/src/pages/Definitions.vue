<template>
  <div>
    <h1>Définitions de workflows</h1>
    <p>Gabarits de workflows gradués pour les équipes autorisées.</p>

    <div class="utd-row mb-32">
      <div class="utd-col">
        <utd-champ-form libelle="Filtrer par équipe">
          <select @change="filtrerEquipe($event.target.value)">
            <option value="">Toutes les équipes</option>
            <option v-for="eq in equipes" :key="eq" :value="eq">{{ eq }}</option>
          </select>
        </utd-champ-form>
      </div>
    </div>

    <table class="utd-tableau" v-if="definitions.length">
      <thead>
        <tr>
          <th>Nom</th>
          <th>Description</th>
          <th>Équipe</th>
          <th>Version</th>
          <th>Dernière modification</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="def in definitions" :key="def.id">
          <td>
            <router-link :to="`/definitions/${def.id}`">{{ def.nom }}</router-link>
          </td>
          <td>{{ def.description || '—' }}</td>
          <td>{{ def.equipe || '—' }}</td>
          <td><code>{{ def.hashVersion.substring(0, 8) }}</code></td>
          <td>{{ formatDate(def.dateModification) }}</td>
          <td>
            <button class="utd-btn utd-btn-secondaire utd-btn-sm"
                    @click="lancerWorkflow(def.id)">Démarrer</button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-else-if="!store.chargement">Aucune définition trouvée.</p>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useWorkflowStore } from '../stores/workflow'
import { useRouter } from 'vue-router'

const store = useWorkflowStore()
const router = useRouter()
const definitions = computed(() => store.definitions)
const equipes = computed(() => [...new Set(store.definitions.map(d => d.equipe).filter(Boolean))])

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

function filtrerEquipe(equipe) {
  store.chargerDefinitions(equipe || undefined)
}

async function lancerWorkflow(defId) {
  const instance = await store.demarrerWorkflow(defId)
  if (instance) router.push(`/instances/${instance.id}`)
}

onMounted(() => store.chargerDefinitions())
</script>
