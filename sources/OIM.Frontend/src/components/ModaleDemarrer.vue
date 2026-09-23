<template>
  <Dialogue id="dialogueDemarrer" :titre="`Démarrer « ${processus} »`" :visible="visible" @update:visible="$emit('update:visible', $event)">
    <utd-avis v-if="erreurs.length" type="erreur" titre="Les entrées sont invalides.">
      <ul>
        <li v-for="e in erreurs" :key="e">{{ e }}</li>
      </ul>
    </utd-avis>

    <p v-if="!entrees.length" class="texte-attenue">Ce processus ne déclare aucune entrée.</p>

    <!-- Formulaire généré à partir de la section « entrees » -->
    <utd-champ-form
      v-for="e in entrees"
      :key="e.uid"
      :id="`champEntree_${e.nom}`"
      :libelle="e.libelle || e.nom"
      :obligatoire="e.requis ? 'true' : 'false'"
      :precision="precision(e)"
      format="lg"
    >
      <select v-if="e.type === 'bool'" v-model="valeurs[e.nom]">
        <option value="">—</option>
        <option value="true">Oui</option>
        <option value="false">Non</option>
      </select>
      <textarea v-else-if="e.type === 'object' || e.type === 'array'" v-model="valeurs[e.nom]" rows="4" class="texte-mono"></textarea>
      <input v-else v-model="valeurs[e.nom]" :type="e.type === 'date' ? 'date' : e.type === 'int' || e.type === 'number' ? 'number' : 'text'" />
    </utd-champ-form>

    <utd-champ-form
      id="champIdInstanceForm"
      libelle="Identifiant de l’instance"
      precision="Optionnel. Ex. dossier-123 : empêche de démarrer deux instances actives pour le même dossier."
      format="lg"
    >
      <input v-model="instanceId" type="text" class="texte-mono" />
    </utd-champ-form>

    <template #pied>
      <button type="button" class="utd-btn secondaire compact" @click="$emit('update:visible', false)">Annuler</button>
      <button type="button" class="utd-btn primaire compact" :disabled="envoi" @click="demarrer">Démarrer</button>
    </template>
  </Dialogue>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import Dialogue from './Dialogue.vue'
import { api, ErreurApi } from '@/lib/api'
import type { EntreeModele } from '@/lib/modele'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ visible: boolean; processus: string; version?: number; entrees: EntreeModele[] }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void }>()
const router = useRouter()

const valeurs = ref<Record<string, string>>({})
const instanceId = ref('')
const erreurs = ref<string[]>([])
const envoi = ref(false)

watch(
  () => props.visible,
  (v) => {
    if (!v) return
    erreurs.value = []
    valeurs.value = Object.fromEntries(props.entrees.map((e) => [e.nom, e.defaut === undefined ? '' : String(e.defaut)]))
  }
)

const precision = (e: EntreeModele): string =>
  [e.description, `Type : ${e.type}`].filter(Boolean).join(' — ')

const demarrer = async (): Promise<void> => {
  envoi.value = true
  erreurs.value = []
  try {
    // Le serveur convertit les types (ex. "42" → 42) : les valeurs vides sont omises.
    const corps: Record<string, unknown> = {}
    for (const [k, v] of Object.entries(valeurs.value)) if (v !== '') corps[k] = v
    const r = await api.demarrer(props.processus, corps, { version: props.version, instanceId: instanceId.value || undefined })
    MessagesHelpers.notifierSucces(`Instance « ${r.instanceId} » démarrée (v${r.version}).`)
    emit('update:visible', false)
    router.push(`/instances/${encodeURIComponent(r.instanceId)}`)
  } catch (e) {
    if (e instanceof ErreurApi && e.details.length) erreurs.value = e.details
    else erreurs.value = [(e as Error).message]
  } finally {
    envoi.value = false
  }
}
</script>
