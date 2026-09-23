<template>
  <aside class="concepteur-palette" aria-labelledby="titrePalette">
    <div class="panneau-entete">
      <h2 id="titrePalette">Étapes</h2>
      <p class="texte-attenue">Glissez une étape sur le canevas, ou utilisez « Ajouter ».</p>
    </div>

    <div class="palette-liste">
      <div
        v-for="t in TYPES_ETAPES"
        :key="t.type"
        class="palette-item"
        :style="{ borderLeftColor: t.couleur }"
        draggable="true"
        @dragstart="debuter($event, t.type)"
      >
        <span class="palette-item-titre" :style="{ color: t.couleur }">
          <span class="icone-etape" aria-hidden="true">{{ t.icone }}</span>{{ t.libelle }}
        </span>
        <span class="palette-item-desc">{{ t.description }}</span>
        <button type="button" class="utd-btn tertiaire comme-lien compact" @click="$emit('ajouter', t.type)">
          Ajouter<span class="utd-sr-only"> une étape {{ t.libelle }}</span>
        </button>
      </div>
    </div>

    <div class="palette-legende">
      <p class="mb-8"><strong>Relier les étapes</strong> : tirez depuis une poignée de sortie vers le haut d’une étape.</p>
      <div class="palette-legende-ligne"><span class="vue-flow__handle poignee-suivant" style="position: static; transform: none"></span>Suivant / sinon</div>
      <div class="palette-legende-ligne"><span class="vue-flow__handle poignee-branche" style="position: static; transform: none"></span>Branche conditionnelle</div>
      <div class="palette-legende-ligne"><span class="vue-flow__handle poignee-erreur" style="position: static; transform: none"></span>Si erreur</div>
      <div class="palette-legende-ligne"><span class="vue-flow__handle poignee-delai" style="position: static; transform: none"></span>Délai expiré</div>
      <p class="mt-8">Sélectionnez un lien et appuyez sur <kbd>Suppr</kbd> pour le retirer.</p>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { TYPES_ETAPES, typeEtape } from '@/lib/catalogue'

defineEmits<{ (e: 'ajouter', type: string): void }>()

/**
 * Image de glisser : un aperçu du noeud tel qu'il apparaîtra sur le canevas, plutôt que la
 * capture de la tuile de palette que le navigateur génère par défaut.
 */
const creerApercu = (type: string): HTMLElement => {
  const t = typeEtape(type)
  const apercu = document.createElement('div')
  apercu.className = 'noeud-etape apercu-glisser'
  apercu.style.borderColor = t.couleur
  apercu.innerHTML = `
    <div class="noeud-entete" style="background-color:${t.couleur}14;color:${t.couleur}">
      <span class="noeud-entete-type"><span class="icone-etape">${t.icone}</span>${t.libelle}</span>
    </div>
    <div class="noeud-corps"><div class="noeud-titre">${type}</div><div class="noeud-resume">Déposez sur le canevas</div></div>`
  document.body.appendChild(apercu)
  return apercu
}

const debuter = (ev: DragEvent, type: string): void => {
  if (!ev.dataTransfer) return
  ev.dataTransfer.setData('application/oim-etape', type)
  ev.dataTransfer.effectAllowed = 'move'

  const apercu = creerApercu(type)
  // Le point de saisie correspond au centre de l'entête, comme lors du dépôt (surDepot).
  ev.dataTransfer.setDragImage(apercu, 115, 14)
  // Le navigateur capture l'image au moment de l'appel : l'élément peut être retiré ensuite.
  requestAnimationFrame(() => apercu.remove())
}
</script>
