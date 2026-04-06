<template>
  <div v-if="instance">
    <div class="utd-row mb-16">
      <div class="utd-col">
        <h1>{{ instance.nomWorkflow }}</h1>
        <div class="utd-row">
          <div class="utd-col-auto"><strong>Instance :</strong> <code>{{ instance.id }}</code></div>
          <div class="utd-col-auto"><strong>CorrelationId :</strong> <code>{{ instance.correlationId }}</code></div>
          <div class="utd-col-auto"><strong>Config :</strong> <code>{{ instance.hashVersionConfig?.substring(0, 8) }}</code></div>
          <div class="utd-col-auto">
            <strong>État :</strong> <EtatBadge :etat="instance.etat" />
          </div>
        </div>
      </div>
    </div>

    <!-- Actions -->
    <div class="mb-32">
      <template v-if="instance.etat === 'EnErreur' || instance.etat === 'EnPause'">
        <button class="utd-btn utd-btn-principal" @click="reprendre">Reprendre le workflow</button>
        <button class="utd-btn utd-btn-secondaire" @click="annuler" style="margin-left: 8px;">Annuler</button>
      </template>
      <button class="utd-btn utd-btn-secondaire" @click="exporterTest" style="margin-left: 8px;">
        Exporter comme test .md
      </button>
    </div>

    <!-- Tâches -->
    <utd-section reduit="false" titre="Tâches">
      <div v-for="tache in instance.taches" :key="tache.id" class="tache-card mb-16">
        <div class="tache-header">
          <strong>{{ tache.nomTache }}</strong>
          <span class="utd-text-sm utd-emphase-gris">{{ tache.typeConnecteur }}</span>
          <EtatBadge :etat="tache.etat" />
        </div>

        <div class="tache-details" v-if="tacheOuverte === tache.id">
          <div class="utd-row">
            <div class="utd-col">
              <h4>INPUT</h4>
              <textarea v-if="tache.etat === 'EnErreur'"
                        v-model="tache.donneesEntree"
                        class="donnees-editor" rows="6"></textarea>
              <pre v-else class="donnees-block">{{ formatJson(tache.donneesEntree) }}</pre>
            </div>
            <div class="utd-col">
              <h4>OUTPUT</h4>
              <pre class="donnees-block">{{ formatJson(tache.donneesSortie) }}</pre>
            </div>
          </div>
          <div v-if="tache.erreur" class="erreur-detail">
            <utd-avis type="erreur" :titre="tache.erreur"></utd-avis>
          </div>
          <div v-if="tache.etat === 'EnErreur'" class="mt-16">
            <button class="utd-btn utd-btn-principal utd-btn-sm"
                    @click="reprendreTache(tache)">Reprendre cette tâche</button>
          </div>
        </div>

        <button class="utd-btn utd-btn-lien utd-btn-sm"
                @click="tacheOuverte = tacheOuverte === tache.id ? null : tache.id">
          {{ tacheOuverte === tache.id ? 'Masquer' : 'Détails' }}
        </button>
      </div>
    </utd-section>

    <!-- Correction en lot -->
    <utd-section v-if="tachesEnErreur.length > 1" titre="Correction en lot">
      <utd-avis type="information" titre="Vous pouvez corriger toutes les tâches en erreur en un seul lot."></utd-avis>
      <button class="utd-btn utd-btn-principal mt-16" @click="patchToutesLesErreurs">
        Reprendre les {{ tachesEnErreur.length }} tâches en erreur
      </button>
    </utd-section>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'
import { useSignalR } from '../composables/useSignalR'
import EtatBadge from '../components/EtatBadge.vue'

const route = useRoute()
const store = useWorkflowStore()
const { rejoindreInstance, quitterInstance, onChangementEtat, onTacheDemarree, onTacheTerminee, onTacheEnErreur, offAll } = useSignalR()

const instance = ref(null)
const tacheOuverte = ref(null)
const tachesEnErreur = computed(() => instance.value?.taches?.filter(t => t.etat === 'EnErreur') || [])

function formatJson(str) {
  if (!str) return '—'
  try { return JSON.stringify(JSON.parse(str), null, 2) } catch { return str }
}

async function charger() {
  instance.value = await store.obtenirInstance(route.params.id)
}

async function reprendre() {
  await store.reprendreInstance(instance.value.id)
  await charger()
}

async function annuler() {
  await store.annulerInstance(instance.value.id)
  await charger()
}

async function exporterTest() {
  await store.exporterInstanceCommeTest(instance.value.id)
}

async function reprendreTache(tache) {
  await store.reprendreTache(instance.value.id, tache.nomTache, tache.donneesEntree)
  await charger()
}

async function patchToutesLesErreurs() {
  const patches = tachesEnErreur.value.map(t => ({
    tacheId: t.id,
    donneesEntreeCorrigees: t.donneesEntree
  }))
  await store.patchTaches(instance.value.id, patches)
  await charger()
}

onMounted(async () => {
  await charger()
  rejoindreInstance(route.params.id)

  onChangementEtat(() => charger())
  onTacheDemarree(() => charger())
  onTacheTerminee(() => charger())
  onTacheEnErreur(() => charger())
})

onUnmounted(() => {
  quitterInstance(route.params.id)
  offAll()
})
</script>

<style scoped>
.tache-card {
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  padding: 12px 16px;
}
.tache-header {
  display: flex;
  align-items: center;
  gap: 12px;
}
.donnees-block {
  background: #f5f5f5;
  padding: 0.75rem;
  border-radius: 4px;
  font-size: 0.8rem;
  max-height: 300px;
  overflow: auto;
}
.donnees-editor {
  width: 100%;
  font-family: monospace;
  font-size: 0.8rem;
  padding: 0.75rem;
  border: 1px solid #ccc;
  border-radius: 4px;
}
.erreur-detail { margin-top: 12px; }
.mt-16 { margin-top: 16px; }
</style>
