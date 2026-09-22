<template>
  <div ref="container" class="graphe-container">
    <p v-if="erreur" style="color:#c62828;font-size:0.85rem;">{{ erreur }}</p>
  </div>
</template>

<script setup>
import { ref, onMounted, watch } from 'vue'

const props = defineProps({ contenu: String })
const container = ref(null)
const erreur = ref(null)
let seq = 0

async function rendre() {
  if (!container.value || !props.contenu?.trim()) return
  erreur.value = null
  try {
    const mermaid = window.mermaid
    if (!mermaid) { erreur.value = 'Mermaid non chargé'; return }
    mermaid.initialize({ startOnLoad: false, theme: 'neutral', flowchart: { curve: 'basis' } })
    const id = `graphe-oam-${++seq}`
    document.getElementById(id)?.remove()
    const { svg } = await mermaid.render(id, props.contenu)
    if (container.value) container.value.innerHTML = svg
  } catch (e) {
    erreur.value = `Impossible de rendre le graphe : ${e.message}`
  }
}

onMounted(rendre)
watch(() => props.contenu, rendre)
</script>

<style scoped>
.graphe-container {
  overflow-x: auto;
  padding: 1rem 0;
}
.graphe-container :deep(svg) {
  max-width: 100%;
  height: auto;
}
</style>
