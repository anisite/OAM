<template>
  <!-- Enrobage de utd-dialog : ouverture/fermeture par attribut (même approche que GED/Conductor). -->
  <utd-dialog ref="dialogue" :id="id" :titre="titre" :id-focus-ouverture="idFocus" :taille="taille" @fermeture="$emit('update:visible', false)">
    <slot />
    <div slot="pied">
      <slot name="pied" />
    </div>
  </utd-dialog>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'

const props = defineProps<{ id: string; titre: string; visible: boolean; idFocus?: string; taille?: string }>()
defineEmits<{ (e: 'update:visible', v: boolean): void }>()

const dialogue = ref<HTMLElement | null>(null)

const appliquer = (visible: boolean): void => {
  if (visible) dialogue.value?.setAttribute('afficher', 'true')
  else dialogue.value?.removeAttribute('afficher')
}

watch(() => props.visible, appliquer)
onMounted(() => appliquer(props.visible))
</script>
