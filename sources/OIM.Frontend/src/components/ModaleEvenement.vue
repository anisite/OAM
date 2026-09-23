<template>
  <Dialogue id="dialogueEvenement" titre="Envoyer un événement" :visible="visible" id-focus="champNomEvenement" @update:visible="$emit('update:visible', $event)">
    <p class="texte-attenue">
      Instance <span class="texte-mono">{{ instanceId }}</span>.
      <template v-if="attendu">L’instance attend l’événement « <strong>{{ attendu }}</strong> ».</template>
    </p>

    <utd-champ-form id="champNomEvenementForm" libelle="Nom de l’événement" obligatoire="true" format="lg">
      <input id="champNomEvenement" v-model="nom" type="text" class="texte-mono" />
    </utd-champ-form>

    <div v-if="suggestions.length" class="barre-actions mb-16">
      <span class="texte-attenue">Modèles :</span>
      <button v-for="s in suggestions" :key="s.libelle" type="button" class="utd-btn tertiaire comme-lien compact" @click="donnees = s.json">
        {{ s.libelle }}
      </button>
    </div>

    <utd-champ-form
      id="champDonneesEvenementForm"
      libelle="Données (JSON)"
      precision="Accessibles dans le processus via « evenement ». Ex. evenement.approuve"
      :invalide="jsonInvalide ? 'true' : 'false'"
      message-erreur="Le JSON est invalide."
    >
      <textarea v-model="donnees" rows="8" class="texte-mono"></textarea>
    </utd-champ-form>

    <template #pied>
      <button type="button" class="utd-btn secondaire compact" @click="$emit('update:visible', false)">Annuler</button>
      <button type="button" class="utd-btn primaire compact" :disabled="!nom || jsonInvalide || envoi" @click="envoyer">Envoyer</button>
    </template>
  </Dialogue>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Dialogue from './Dialogue.vue'
import { api } from '@/lib/api'
import MessagesHelpers from '@/helpers/messages'

const props = defineProps<{ visible: boolean; instanceId: string; attendu?: string }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'envoye'): void }>()

const nom = ref('')
const donnees = ref('{\n  \n}')
const envoi = ref(false)

watch(
  () => props.visible,
  (v) => {
    if (v) nom.value = props.attendu ?? nom.value
  }
)

const suggestions = computed(() =>
  nom.value === 'decision'
    ? [
        { libelle: 'Approuver', json: '{\n  "approuve": true,\n  "commentaire": ""\n}' },
        { libelle: 'Refuser', json: '{\n  "approuve": false,\n  "motif": ""\n}' }
      ]
    : []
)

const jsonInvalide = computed(() => {
  if (!donnees.value.trim()) return false
  try {
    JSON.parse(donnees.value)
    return false
  } catch {
    return true
  }
})

const envoyer = async (): Promise<void> => {
  envoi.value = true
  try {
    await api.evenement(props.instanceId, nom.value, donnees.value.trim() ? JSON.parse(donnees.value) : null)
    MessagesHelpers.notifierSucces(`L’événement « ${nom.value} » a été transmis.`)
    emit('update:visible', false)
    emit('envoye')
  } catch (e) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Envoi impossible')
  } finally {
    envoi.value = false
  }
}
</script>
