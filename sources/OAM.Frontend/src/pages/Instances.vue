<template>
  <div>
    <h1>Instances de workflows</h1>

    <div class="utd-row mb-32">
      <div class="utd-col-auto">
        <utd-champ-form libelle="État">
          <select v-model="filtreEtat" @change="appliquerFiltreEtat">
            <option value="">Tous</option>
            <option value="EnCours">En cours</option>
            <option value="EnErreur">En erreur</option>
            <option value="EnPause">En pause</option>
            <option value="Termine">Terminé</option>
          </select>
        </utd-champ-form>
      </div>
    </div>

    <table ref="tableRef" class="utd-table" style="width:100%">
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
    </table>
  </div>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useRouter } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'
import { useSignalR } from '../composables/useSignalR'

const route = useRoute()
const router = useRouter()
const store = useWorkflowStore()
const { onChangementEtatGlobal, offAll } = useSignalR()
const tableRef = ref(null)
const filtreEtat = ref(route.query.etat || '')
let dtInstance = null

const etatNoms = ['EnAttente', 'EnCours', 'EnPause', 'EnErreur', 'Termine', 'Annule']
const libellesEtat = {
  EnAttente: 'En attente', EnCours: 'En cours', EnPause: 'En pause',
  EnErreur: 'En erreur', Termine: 'Terminé', Annule: 'Annulé'
}

function badgeHtml(etat) {
  const etatStr = typeof etat === 'number' ? (etatNoms[etat] ?? String(etat)) : etat
  return `<span class="etat-badge etat-${etatStr.toLowerCase()}">${libellesEtat[etatStr] || etatStr}</span>`
}

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

function tachesHtml(taches) {
  if (!taches?.length) return '0/0'
  const reussies = taches.filter(t => t.etat === 'Reussie').length
  const erreurs = taches.filter(t => t.etat === 'EnErreur').length
  let html = `${reussies}/${taches.length}`
  if (erreurs) html += ` <span style="color:#c62828;font-weight:600">(${erreurs} erreur(s))</span>`
  return html
}

function initDatatable() {
  if (dtInstance) { dtInstance.destroy(); dtInstance = null }
  if (window.utd?.datatables) window.utd.datatables.definirParametresDefaut()
  dtInstance = new window.DataTable(tableRef.value, {
    data: store.instances,
    order: [[4, 'desc']],
    columns: [
      { data: 'nomWorkflow', render: (d, t, row) => t === 'display' ? `<a href="/instances/${row.id}" class="dt-nav">${d}</a>` : d },
      { data: 'correlationId', render: (d, t) => t === 'display' ? `<code>${d}</code>` : d },
      { data: 'etat', render: (d, t) => t === 'display' ? badgeHtml(d) : (typeof d === 'number' ? (etatNoms[d] ?? String(d)) : d) },
      { data: 'taches', orderable: false, searchable: false, render: (d, t) => t === 'display' ? tachesHtml(d) : '' },
      { data: 'dateDebut', render: (d, t) => t === 'display' ? formatDate(d) : d },
      {
        data: null, orderable: false, searchable: false,
        render: (d, t, row) => {
          const peutReprendre = row.etat === 'EnErreur' || row.etat === 'EnPause'
          return peutReprendre ? `<button class="utd-btn utd-btn-secondaire utd-btn-sm" data-inst-id="${row.id}">Reprendre</button>` : ''
        }
      }
    ]
  })
  if (filtreEtat.value) dtInstance.column(2).search(filtreEtat.value).draw()
}

function appliquerFiltreEtat() {
  if (dtInstance) dtInstance.column(2).search(filtreEtat.value).draw()
}

async function charger() {
  await store.chargerInstances()
  initDatatable()
}

onMounted(async () => {
  await charger()

  tableRef.value.addEventListener('click', async e => {
    const a = e.target.closest('a.dt-nav')
    if (a) { e.preventDefault(); router.push(a.getAttribute('href')) }

    const btn = e.target.closest('[data-inst-id]')
    if (btn) {
      await store.reprendreInstance(btn.dataset.instId)
      await charger()
    }
  })

  onChangementEtatGlobal(() => charger())
})

onUnmounted(() => {
  offAll()
  if (dtInstance) { dtInstance.destroy(); dtInstance = null }
})
</script>
