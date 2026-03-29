import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/auth'

const connection = ref(null)
const estConnecte = ref(false)

export function useSignalR() {
  const authStore = useAuthStore()

  async function connecter() {
    if (connection.value) return

    connection.value = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/workflow', {
        accessTokenFactory: () => authStore.token
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build()

    connection.value.onreconnected(() => { estConnecte.value = true })
    connection.value.onclose(() => { estConnecte.value = false })

    try {
      await connection.value.start()
      estConnecte.value = true
    } catch (err) {
      console.error('SignalR connexion échouée:', err)
    }
  }

  function rejoindreInstance(instanceId) {
    connection.value?.invoke('RejoindreInstance', instanceId)
  }

  function quitterInstance(instanceId) {
    connection.value?.invoke('QuitterInstance', instanceId)
  }

  function rejoindreCorrelation(correlationId) {
    connection.value?.invoke('RejoindreCorrelation', correlationId)
  }

  function onChangementEtat(callback) {
    connection.value?.on('ChangementEtat', callback)
  }

  function onChangementEtatGlobal(callback) {
    connection.value?.on('ChangementEtatGlobal', callback)
  }

  function onTacheDemarree(callback) {
    connection.value?.on('TacheDemarree', callback)
  }

  function onTacheTerminee(callback) {
    connection.value?.on('TacheTerminee', callback)
  }

  function onTacheEnErreur(callback) {
    connection.value?.on('TacheEnErreur', callback)
  }

  function onWorkflowTermine(callback) {
    connection.value?.on('WorkflowTermine', callback)
  }

  function offAll() {
    if (!connection.value) return
    const events = [
      'ChangementEtat', 'ChangementEtatGlobal', 'TacheDemarree',
      'TacheTerminee', 'TacheEnErreur', 'WorkflowTermine', 'TacheEnErreurGlobal'
    ]
    events.forEach(e => connection.value.off(e))
  }

  return {
    connection, estConnecte, connecter,
    rejoindreInstance, quitterInstance, rejoindreCorrelation,
    onChangementEtat, onChangementEtatGlobal,
    onTacheDemarree, onTacheTerminee, onTacheEnErreur, onWorkflowTermine,
    offAll
  }
}
