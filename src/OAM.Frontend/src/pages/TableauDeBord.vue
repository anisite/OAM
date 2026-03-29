<template>
  <div>
    <h1>Tableau de bord</h1>

    <div class="utd-row utd-row-cols-1 utd-row-cols-md-3 mb-32">
      <div class="utd-col">
        <utd-section titre="Définitions actives">
          <p class="stat-nombre">{{ definitions.length }}</p>
          <router-link to="/definitions">Voir les définitions</router-link>
        </utd-section>
      </div>
      <div class="utd-col">
        <utd-section titre="Instances en cours">
          <p class="stat-nombre">{{ instancesEnCours.length }}</p>
          <router-link to="/instances?etat=EnCours">Voir les instances</router-link>
        </utd-section>
      </div>
      <div class="utd-col">
        <utd-section titre="Instances en erreur">
          <p class="stat-nombre stat-erreur">{{ instancesEnErreur.length }}</p>
          <router-link to="/instances?etat=EnErreur">Gérer les erreurs</router-link>
        </utd-section>
      </div>
    </div>

    <utd-section titre="Activité récente">
      <table class="utd-table" v-if="instances.length">
        <thead>
          <tr>
            <th>Workflow</th>
            <th>CorrelationId</th>
            <th>État</th>
            <th>Date</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="inst in instances.slice(0, 10)" :key="inst.id">
            <td>
              <router-link :to="`/instances/${inst.id}`">{{ inst.nomWorkflow }}</router-link>
            </td>
            <td><code>{{ inst.correlationId }}</code></td>
            <td><EtatBadge :etat="inst.etat" /></td>
            <td>{{ formatDate(inst.dateCreation) }}</td>
          </tr>
        </tbody>
      </table>
      <p v-else>Aucune instance récente.</p>
    </utd-section>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted } from 'vue'
import { useWorkflowStore } from '../stores/workflow'
import { useSignalR } from '../composables/useSignalR'
import EtatBadge from '../components/EtatBadge.vue'

const store = useWorkflowStore()
const { onChangementEtatGlobal, onTacheEnErreurGlobal, onWorkflowTermineGlobal, offAll } = useSignalR()

const definitions = computed(() => store.definitions)
const instances = computed(() => store.instances)
const instancesEnCours = computed(() => store.instances.filter(i => i.etat === 'EnCours'))
const instancesEnErreur = computed(() => store.instancesEnErreur)

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

onMounted(async () => {
  await Promise.all([
    store.chargerDefinitions(),
    store.chargerInstances(),
    store.chargerEnErreur()
  ])

  const rafraichir = () => { store.chargerInstances(); store.chargerEnErreur() }
  onChangementEtatGlobal(rafraichir)
  onTacheEnErreurGlobal(rafraichir)
  onWorkflowTermineGlobal(rafraichir)
})

onUnmounted(() => offAll())
</script>

<style scoped>
.stat-nombre { font-size: 2.5rem; font-weight: 700; margin: 0.5rem 0; }
.stat-erreur { color: #d32f2f; }
</style>
