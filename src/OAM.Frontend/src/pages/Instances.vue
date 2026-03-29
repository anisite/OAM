<template>
  <div>
    <h1>Instances de workflows</h1>

    <div class="utd-row mb-32">
      <div class="utd-col-auto">
        <utd-champ-form libelle="État">
          <select v-model="filtreEtat" @change="charger">
            <option value="">Tous</option>
            <option value="EnCours">En cours</option>
            <option value="EnErreur">En erreur</option>
            <option value="EnPause">En pause</option>
            <option value="Termine">Terminé</option>
          </select>
        </utd-champ-form>
      </div>
    </div>

    <table class="utd-tableau" v-if="instances.length">
      <thead>
        <tr>
          <th>Workflow</th>
          <th>CorrelationId</th>
          <th>État</th>
          <th>Tâches</th>
          <th>Date début</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="inst in instances" :key="inst.id">
          <td>
            <router-link :to="`/instances/${inst.id}`">{{ inst.nomWorkflow }}</router-link>
          </td>
          <td><code>{{ inst.correlationId }}</code></td>
          <td><EtatBadge :etat="inst.etat" /></td>
          <td>
            {{ inst.taches?.filter(t => t.etat === 'Reussie').length || 0 }}/{{ inst.taches?.length || 0 }}
            <span v-if="inst.taches?.some(t => t.etat === 'EnErreur')" class="erreur-count">
              ({{ inst.taches.filter(t => t.etat === 'EnErreur').length }} erreur(s))
            </span>
          </td>
          <td>{{ formatDate(inst.dateDebut) }}</td>
          <td>
            <button v-if="inst.etat === 'EnErreur' || inst.etat === 'EnPause'"
                    class="utd-btn utd-btn-secondaire utd-btn-sm"
                    @click="reprendre(inst.id)">Reprendre</button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-else-if="!store.chargement">Aucune instance trouvée.</p>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'
import { useSignalR } from '../composables/useSignalR'
import EtatBadge from '../components/EtatBadge.vue'

const route = useRoute()
const store = useWorkflowStore()
const { onChangementEtatGlobal, offAll } = useSignalR()

const filtreEtat = ref(route.query.etat || '')
const instances = computed(() => store.instances)

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

async function charger() {
  await store.chargerInstances(filtreEtat.value || undefined)
}

async function reprendre(id) {
  await store.reprendreInstance(id)
  await charger()
}

onMounted(() => {
  charger()
  onChangementEtatGlobal(() => charger())
})

onUnmounted(() => offAll())
</script>

<style scoped>
.erreur-count { color: #c62828; font-weight: 600; }
</style>
