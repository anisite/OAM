<template>
  <div class="barre-rafraichissement">
    <span class="texte-attenue" aria-live="polite">
      <template v-if="derniereMaj">Mis à jour à {{ derniereMaj.toLocaleTimeString('fr-CA') }}</template>
    </span>
    <label class="interrupteur">
      <input type="checkbox" :checked="modelValue" @change="$emit('update:modelValue', ($event.target as HTMLInputElement).checked)" />
      Actualisation automatique
    </label>
    <button type="button" class="utd-btn secondaire compact" :disabled="chargement" @click="$emit('actualiser')">
      Actualiser
    </button>
  </div>
</template>

<script setup lang="ts">
defineProps<{ modelValue: boolean; chargement: boolean; derniereMaj: Date | null }>()
defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'actualiser'): void }>()
</script>
