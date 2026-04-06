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
      <p v-else>Aucune version enregistrée.</p>
    </utd-section>

    <utd-section reduit="false" titre="Cas de tests">
      <p v-if="casTests.length === 0">Aucun cas de test déployé.</p>
      <table class="utd-table" v-else>
        <thead>
          <tr>
            <th>Nom</th>
            <th>Déployé le</th>
            <th>Déployé par</th>
            <th>Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="cas in casTests" :key="cas.id">
            <td>{{ cas.nom }}</td>
            <td>{{ formatDate(cas.dateChargement) }}</td>
            <td>{{ cas.deployePar || '—' }}</td>
            <td>
              <button
                class="utd-btn utd-btn-secondaire"
                :disabled="casEnExecution[cas.nom]"
                @click="lancerTest(cas.nom)"
              >
                {{ casEnExecution[cas.nom] ? 'En cours…' : 'Lancer' }}
              </button>
              <span v-if="casResultat[cas.nom]" :class="casResultat[cas.nom].passe ? 'badge-passe' : 'badge-echoue'">
                {{ casResultat[cas.nom].passe ? '✓ Passé' : '✗ Échoué' }}
              </span>
              <ul v-if="casResultat[cas.nom] && !casResultat[cas.nom].passe" class="differences">
                <li v-for="d in casResultat[cas.nom].differences" :key="d">{{ d }}</li>
              </ul>
              <span v-if="casErreur[cas.nom]" class="erreur-test">{{ casErreur[cas.nom] }}</span>
            </td>
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
import { ref, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'

const route = useRoute()
const router = useRouter()
const store = useWorkflowStore()

const definition = ref(null)
const yamlContenu = ref('')
const versions = ref([])
const casTests = ref([])
const casEnExecution = reactive({})
const casErreur = reactive({})
const casResultat = reactive({})

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

async function demarrer() {
  const instance = await store.demarrerWorkflow(definition.value.id)
  if (instance) router.push(`/instances/${instance.id}`)
}

async function lancerTest(nom) {
  casEnExecution[nom] = true
  casErreur[nom] = null
  try {
    const resultat = await store.executerCasTest(definition.value.id, nom)
    if (resultat) {
      casResultat[nom] = { passe: resultat.passe, differences: resultat.differences ?? [] }
    }
  } catch (e) {
    casErreur[nom] = e.message || 'Erreur lors de l\'exécution'
  } finally {
    casEnExecution[nom] = false
  }
}

onMounted(async () => {
  const id = route.params.id
  const [def, yaml, vers, tests] = await Promise.all([
    store.obtenirDefinition(id),
    store.obtenirYaml(id),
    store.obtenirVersions(id),
    store.listerCasTests(id)
  ])
  definition.value = def
  yamlContenu.value = yaml
  versions.value = vers
  casTests.value = tests ?? []
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

.erreur-test {
  color: #c62828;
  font-size: 0.85rem;
  margin-left: 0.5rem;
}

.badge-passe {
  color: #2e7d32;
  font-weight: 600;
  margin-left: 0.5rem;
}

.badge-echoue {
  color: #c62828;
  font-weight: 600;
  margin-left: 0.5rem;
}

.differences {
  margin: 0.25rem 0 0 0;
  padding-left: 1.25rem;
  font-size: 0.8rem;
  color: #c62828;
}
</style>
