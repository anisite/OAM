import { defineStore } from 'pinia'
import { ref } from 'vue'
import { useAuthStore } from './auth'

export const useWorkflowStore = defineStore('workflow', () => {
  const definitions = ref([])
  const instances = ref([])
  const instancesEnErreur = ref([])
  const chargement = ref(false)

  const authStore = useAuthStore()

  async function apiFetch(url, options = {}) {
    await authStore.initialiser()
    const response = await fetch(url, {
      ...options,
      headers: { 'Content-Type': 'application/json', ...authStore.headers(), ...options.headers }
    })
    if (response.status === 401) {
      await authStore.obtenirToken()
      const retry = await fetch(url, {
        ...options,
        headers: { 'Content-Type': 'application/json', ...authStore.headers(), ...options.headers }
      })
      if (!retry.ok) throw new Error(`${retry.status} ${retry.statusText}`)
      const retryText = await retry.text()
      return retryText ? JSON.parse(retryText) : null
    }
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`)
    const text = await response.text()
    return text ? JSON.parse(text) : null
  }

  // Définitions
  async function chargerDefinitions(equipe) {
    chargement.value = true
    try {
      const params = equipe ? `?equipe=${encodeURIComponent(equipe)}` : ''
      definitions.value = await apiFetch(`/api/definitions${params}`)
    } finally { chargement.value = false }
  }

  async function obtenirDefinition(id) {
    return await apiFetch(`/api/definitions/${id}`)
  }

  async function obtenirYaml(id) {
    const response = await fetch(`/api/definitions/${id}/yaml`, { headers: authStore.headers() })
    return await response.text()
  }

  async function obtenirVersions(id) {
    return await apiFetch(`/api/definitions/${id}/versions`)
  }

  async function deployerDefinition(data) {
    return await apiFetch('/api/definitions/deployer', { method: 'POST', body: JSON.stringify(data) })
  }

  // Instances
  async function chargerInstances(etat, definitionId) {
    chargement.value = true
    try {
      const params = new URLSearchParams()
      if (etat) params.set('etat', etat)
      if (definitionId) params.set('definitionId', definitionId)
      const query = params.toString() ? `?${params}` : ''
      instances.value = await apiFetch(`/api/instances${query}`)
    } finally { chargement.value = false }
  }

  async function obtenirInstance(id) {
    return await apiFetch(`/api/instances/${id}`)
  }

  async function chargerEnErreur(definitionId) {
    const params = definitionId ? `?definitionId=${definitionId}` : ''
    instancesEnErreur.value = await apiFetch(`/api/instances/erreurs${params}`)
  }

  async function demarrerWorkflow(definitionId, donneesEntree, correlationId) {
    return await apiFetch('/api/instances/demarrer', {
      method: 'POST',
      body: JSON.stringify({ definitionId, donneesEntree, correlationId })
    })
  }

  async function reprendreInstance(id) {
    await apiFetch(`/api/instances/${id}/reprendre`, { method: 'POST' })
  }

  async function reprendreTache(instanceId, nomTache, donneesEntreeCorrigees) {
    await apiFetch(`/api/instances/${instanceId}/reprendre-tache`, {
      method: 'POST',
      body: JSON.stringify({ nomTache, donneesEntreeCorrigees })
    })
  }

  async function patchTaches(instanceId, taches) {
    await apiFetch(`/api/instances/${instanceId}/taches`, {
      method: 'PATCH',
      body: JSON.stringify({ taches })
    })
  }

  async function pauserInstance(id) {
    await apiFetch(`/api/instances/${id}/pauser`, { method: 'POST' })
  }

  async function annulerInstance(id) {
    await apiFetch(`/api/instances/${id}/annuler`, { method: 'POST' })
  }

  return {
    definitions, instances, instancesEnErreur, chargement,
    chargerDefinitions, obtenirDefinition, obtenirYaml, obtenirVersions, deployerDefinition,
    chargerInstances, obtenirInstance, chargerEnErreur,
    demarrerWorkflow, reprendreInstance, reprendreTache, patchTaches,
    pauserInstance, annulerInstance
  }
})
