<template>
  <div v-if="definition">
    <h1>{{ definition.nom }}</h1>
    <p v-if="definition.description">{{ definition.description }}</p>

    <div class="utd-row mb-32">
      <div class="utd-col-auto">
        <strong>Équipe :</strong> {{ definition.equipe || '—' }}
      </div>
      <div class="utd-col-auto">
        <strong>Version :</strong> <code>{{ definition.hashVersion.substring(0, 8) }}</code>
      </div>
      <div class="utd-col-auto">
        <strong>Dernière modification :</strong> {{ formatDate(definition.dateModification) }}
      </div>
    </div>

    <utd-section reduit="false" titre="Définition YAML">
      <pre class="yaml-block">{{ yamlContenu }}</pre>
    </utd-section>

    <utd-section reduit="false" titre="Historique des versions">
      <table class="utd-table" v-if="versions.length">
        <thead>
          <tr>
            <th>Hash</th>
            <th>Date de chargement</th>
            <th>Déployé par</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="v in versions" :key="v.id">
            <td><code>{{ v.hashVersion.substring(0, 8) }}</code></td>
            <td>{{ formatDate(v.dateChargement) }}</td>
            <td>{{ v.deployePar || '—' }}</td>
          </tr>
        </tbody>
      </table>
    </utd-section>

    <utd-section reduit="false" titre="Actions">
      <button class="utd-btn utd-btn-principal" @click="demarrer">
        Démarrer une instance
      </button>
    </utd-section>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'

const route = useRoute()
const router = useRouter()
const store = useWorkflowStore()

const definition = ref(null)
const yamlContenu = ref('')
const versions = ref([])

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

async function demarrer() {
  const instance = await store.demarrerWorkflow(definition.value.id)
  if (instance) router.push(`/instances/${instance.id}`)
}

onMounted(async () => {
  const id = route.params.id
  const [def, yaml, vers] = await Promise.all([
    store.obtenirDefinition(id),
    store.obtenirYaml(id),
    store.obtenirVersions(id)
  ])
  definition.value = def
  yamlContenu.value = yaml
  versions.value = vers
})
</script>

<style scoped>
.yaml-block {
  background: #f5f5f5;
  padding: 1rem;
  border-radius: 4px;
  overflow-x: auto;
  font-size: 0.85rem;
  line-height: 1.5;
}
</style>
