<template>
  <!-- Éditeur clé → valeur pour donnees, variables, entrees d'un sous-processus.
       Les valeurs sont des textes pouvant contenir des expressions {{ … }}. -->
  <div>
    <div v-for="(ligne, i) in lignes" :key="i" class="ligne-kv">
      <input :value="ligne.cle" type="text" class="texte-mono" :aria-label="`Clé ${i + 1}`" placeholder="clé" @change="majCle(i, ($event.target as HTMLInputElement).value)" />
      <input :value="ligne.valeur" type="text" class="texte-mono" :aria-label="`Valeur de ${ligne.cle}`" placeholder="{{ entrees.x }}" @change="majValeur(i, ($event.target as HTMLInputElement).value)" />
      <button type="button" class="btn-icone" :title="`Retirer ${ligne.cle}`" @click="retirer(i)">
        <span aria-hidden="true">✕</span><span class="utd-sr-only">Retirer {{ ligne.cle }}</span>
      </button>
    </div>
    <button type="button" class="utd-btn tertiaire comme-lien compact" @click="ajouter">+ Ajouter une valeur</button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ modelValue: Record<string, any> | undefined | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Record<string, any>): void }>()

const texte = (v: unknown): string => (typeof v === 'string' ? v : v === undefined || v === null ? '' : JSON.stringify(v))

const lignes = computed(() => Object.entries(props.modelValue ?? {}).map(([cle, valeur]) => ({ cle, valeur: texte(valeur) })))

// Une valeur JSON (nombre, booléen, objet) est conservée typée; sinon c'est un texte.
const lire = (s: string): unknown => {
  if (/^(-?\d+(\.\d+)?|true|false|null|\{.*\}|\[.*\])$/.test(s.trim())) {
    try {
      return JSON.parse(s)
    } catch {
      /* texte */
    }
  }
  return s
}

const emettre = (l: { cle: string; valeur: string }[]): void => {
  const o: Record<string, any> = {}
  for (const x of l) if (x.cle) o[x.cle] = lire(x.valeur)
  emit('update:modelValue', o)
}

const majCle = (i: number, cle: string): void => emettre(lignes.value.map((l, j) => (j === i ? { ...l, cle } : l)))
const majValeur = (i: number, valeur: string): void => emettre(lignes.value.map((l, j) => (j === i ? { ...l, valeur } : l)))
const retirer = (i: number): void => emettre(lignes.value.filter((_, j) => j !== i))
const ajouter = (): void => {
  let n = 1
  while (lignes.value.some((l) => l.cle === `cle${n}`)) n++
  emettre([...lignes.value, { cle: `cle${n}`, valeur: '' }])
}
</script>
