<template>
  <div>
    <h1>Déploiement</h1>

    <utd-section reduit="false" titre="Workflows">
      <p>Déployez un fichier <code>.zip</code> contenant un ou plusieurs dossiers de workflow
         (<code>NomWorkflow/workflow.yml</code>).</p>
      <div class="upload-zone">
        <input type="file" accept=".zip" ref="inputWorkflow" @change="onFichierWorkflow" style="display:none" />
        <button class="utd-btn utd-btn-secondaire" @click="inputWorkflow.click()">
          Parcourir…
        </button>
        <span class="nom-fichier">{{ nomFichierWorkflow || 'Aucun fichier sélectionné' }}</span>
        <button class="utd-btn utd-btn-principal" :disabled="!fichierWorkflow || chargementWorkflow" @click="deployerWorkflow">
          {{ chargementWorkflow ? 'Déploiement…' : 'Déployer' }}
        </button>
      </div>
      <div v-if="resultatWorkflow" class="mt-16">
        <utd-avis v-if="resultatWorkflow.erreurs?.length" type="erreur"
          :titre="`${resultatWorkflow.erreurs.length} erreur(s)`">
          <ul><li v-for="e in resultatWorkflow.erreurs" :key="e">{{ e }}</li></ul>
        </utd-avis>
        <utd-avis v-if="resultatWorkflow.deployes?.length" type="succes"
          :titre="`${resultatWorkflow.deployes.length} workflow(s) déployé(s)`">
          <ul><li v-for="d in resultatWorkflow.deployes" :key="d.workflow">{{ d.workflow }}</li></ul>
        </utd-avis>
      </div>
    </utd-section>

    <utd-section reduit="false" titre="Cas de tests">
      <p>Déployez un fichier <code>.zip</code> contenant les cas de tests
         (<code>NomWorkflow/tests/*.md</code>).</p>
      <div class="upload-zone">
        <input type="file" accept=".zip" ref="inputTests" @change="onFichierTests" style="display:none" />
        <button class="utd-btn utd-btn-secondaire" @click="inputTests.click()">
          Parcourir…
        </button>
        <span class="nom-fichier">{{ nomFichierTests || 'Aucun fichier sélectionné' }}</span>
        <button class="utd-btn utd-btn-principal" :disabled="!fichierTests || chargementTests" @click="deployerTests">
          {{ chargementTests ? 'Déploiement…' : 'Déployer' }}
        </button>
      </div>
      <div v-if="resultatTests" class="mt-16">
        <utd-avis v-if="resultatTests.erreurs?.length" type="erreur"
          :titre="`${resultatTests.erreurs.length} erreur(s)`">
          <ul><li v-for="e in resultatTests.erreurs" :key="e">{{ e }}</li></ul>
        </utd-avis>
        <utd-avis v-if="resultatTests.deployes?.length" type="succes"
          :titre="`${resultatTests.deployes.length} workflow(s) mis à jour`">
          <ul>
            <li v-for="d in resultatTests.deployes" :key="d.workflow">
              {{ d.workflow }} — {{ d.tests?.join(', ') }}
            </li>
          </ul>
        </utd-avis>
      </div>
    </utd-section>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useWorkflowStore } from '../stores/workflow'

const store = useWorkflowStore()

const inputWorkflow = ref(null)
const fichierWorkflow = ref(null)
const nomFichierWorkflow = ref('')
const chargementWorkflow = ref(false)
const resultatWorkflow = ref(null)

const inputTests = ref(null)
const fichierTests = ref(null)
const nomFichierTests = ref('')
const chargementTests = ref(false)
const resultatTests = ref(null)

function onFichierWorkflow(e) {
  fichierWorkflow.value = e.target.files[0] ?? null
  nomFichierWorkflow.value = fichierWorkflow.value?.name ?? ''
  resultatWorkflow.value = null
}

function onFichierTests(e) {
  fichierTests.value = e.target.files[0] ?? null
  nomFichierTests.value = fichierTests.value?.name ?? ''
  resultatTests.value = null
}

async function deployerWorkflow() {
  if (!fichierWorkflow.value) return
  chargementWorkflow.value = true
  try {
    resultatWorkflow.value = await store.deployerDefinition(fichierWorkflow.value)
  } catch (e) {
    resultatWorkflow.value = { erreurs: [e.message], deployes: [] }
  } finally {
    chargementWorkflow.value = false
  }
}

async function deployerTests() {
  if (!fichierTests.value) return
  chargementTests.value = true
  try {
    resultatTests.value = await store.deployerCasTests(fichierTests.value)
  } catch (e) {
    resultatTests.value = { erreurs: [e.message], deployes: [] }
  } finally {
    chargementTests.value = false
  }
}
</script>

<style scoped>
.upload-zone {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}
.nom-fichier {
  color: #555;
  font-size: 0.9rem;
}
.mt-16 { margin-top: 16px; }
</style>
