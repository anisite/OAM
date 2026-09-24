<template>
  <div class="utd-page-texte">
    <h1>Mon profil</h1>

    <template v-if="moi">
      <utd-section extensible="false" titre="Identité">
        <dl class="grille-infos">
          <div><dt>Utilisateur</dt><dd class="texte-mono">{{ moi.utilisateur }}</dd></div>
          <div><dt>Rôle</dt><dd>{{ role }}</dd></div>
        </dl>
      </utd-section>

      <utd-section extensible="false" titre="Mes équipes">
        <p v-if="moi.admin || moi.support" class="texte-attenue">
          {{ moi.admin ? 'En tant qu’administrateur' : 'En tant que membre du support' }} OIM, vous avez accès à toutes les équipes.
        </p>
        <p v-if="!moi.equipes.length" class="zone-vide">Vous n’êtes membre d’aucune équipe.</p>
        <ul v-else>
          <li v-for="e in moi.equipes" :key="e">
            <router-link :to="`/${encodeURIComponent(e)}`">{{ noms[e] ?? e }}</router-link>
            <span class="texte-attenue texte-mono"> {{ e }}</span>
          </li>
        </ul>
      </utd-section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '@/lib/api'
import { useFilAriane } from '@/lib/filAriane'
import { chargerMoi, moi } from '@/lib/session'

useFilAriane(() => [{ libelle: 'Accueil', lien: '/' }, { libelle: 'Mon profil' }])

const noms = ref<Record<string, string>>({})

const role = computed(() =>
  moi.value?.admin ? 'Administrateur OIM' : moi.value?.support ? 'Support OIM' : 'Membre d’équipe'
)

onMounted(async () => {
  await chargerMoi()
  const equipes = await api.equipes().catch(() => [])
  noms.value = Object.fromEntries(equipes.map((e) => [e.id, e.nom]))
})
</script>
