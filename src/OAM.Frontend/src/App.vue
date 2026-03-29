<template>
  <div class="utd-conteneur-principal">
    <header>
      <utd-piv-entete></utd-piv-entete>
      <div class="utd-bandeau-principal">
        <utd-menu-horizontal :path-courant="$route.path">
          <router-link to="/">Tableau de bord</router-link>
          <utd-menu-horizontal-item libelle="Définitions" href="/definitions">
            <router-link to="/definitions">Définitions</router-link>
          </utd-menu-horizontal-item>
          <utd-menu-horizontal-item libelle="Instances" href="/instances">
            <router-link to="/instances">Instances</router-link>
          </utd-menu-horizontal-item>
        </utd-menu-horizontal>
        <div class="utd-zone-raccourcis-connexion">
          <div class="utd-zone-connexion">
            <span v-if="authStore.utilisateur">{{ authStore.utilisateur }}</span>
          </div>
        </div>
      </div>
    </header>

    <main id="main">
      <div class="utd-container">
        <router-view />
      </div>
    </main>

    <utd-hautpage></utd-hautpage>

    <footer class="utd">
      <utd-pied-page-site></utd-pied-page-site>
      <utd-piv-pied-page></utd-piv-pied-page>
    </footer>
  </div>
</template>

<script setup>
import { onMounted } from 'vue'
import { useAuthStore } from './stores/auth'
import { useSignalR } from './composables/useSignalR'

const authStore = useAuthStore()
const { connecter } = useSignalR()

onMounted(async () => {
  await authStore.initialiser()
  if (authStore.token) {
    await connecter()
  }
})
</script>
