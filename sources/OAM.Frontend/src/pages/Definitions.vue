<template>
  <div>
    <h1>Définitions de workflows</h1>
    <table ref="tableRef" class="utd-table" style="width:100%">
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
    </table>
  </div>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { useWorkflowStore } from '../stores/workflow'
import { useRouter } from 'vue-router'

const store = useWorkflowStore()
const router = useRouter()
const tableRef = ref(null)
let dtInstance = null

function formatDate(d) {
  return d ? new Date(d).toLocaleString('fr-CA') : ''
}

function initDatatable() {
  if (dtInstance) { dtInstance.destroy(); dtInstance = null }
  if (window.utd?.datatables) window.utd.datatables.definirParametresDefaut()
  dtInstance = new window.DataTable(tableRef.value, {
    data: store.definitions,
    columns: [
      { data: 'nom', render: (d, t, row) => t === 'display' ? `<a href="/definitions/${row.id}" class="dt-nav">${d}</a>` : d },
      { data: 'description', defaultContent: '—' },
      { data: 'equipe', defaultContent: '—' },
      { data: 'hashVersion', orderable: false, render: (d, t) => t === 'display' ? `<code>${d.substring(0, 8)}</code>` : d },
      { data: 'dateModification', render: (d, t) => t === 'display' ? formatDate(d) : d },
      {
        data: null, orderable: false, searchable: false,
        render: (d, t, row) => `<button class="utd-btn utd-btn-secondaire utd-btn-sm" data-def-id="${row.id}">Démarrer</button>`
      }
    ]
  })
}

onMounted(async () => {
  await store.chargerDefinitions()
  initDatatable()

  tableRef.value.addEventListener('click', async e => {
    const a = e.target.closest('a.dt-nav')
    if (a) { e.preventDefault(); router.push(a.getAttribute('href')) }

    const btn = e.target.closest('[data-def-id]')
    if (btn) {
      const instance = await store.demarrerWorkflow(btn.dataset.defId)
      if (instance) router.push(`/instances/${instance.id}`)
    }
  })
})

onUnmounted(() => { if (dtInstance) { dtInstance.destroy(); dtInstance = null } })
</script>
