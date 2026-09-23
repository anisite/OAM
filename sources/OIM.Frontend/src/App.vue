<template>
  <div>
    <div class="utd-conteneur-principal">
      <header>
        <utd-piv-entete
          id="pivEntete"
          titre-site1="OIM"
          titre-site2="Orchestrateur de processus"
          alt-logo="Signature du gouvernement du Québec. Accédez au tableau de bord OIM."
        >
          <span slot="lienLogo"><router-link to="/">Accueil</router-link></span>
          <span slot="lienTitreSite"><router-link to="/">Accueil</router-link></span>
        </utd-piv-entete>

        <div class="utd-bandeau-principal" id="bandeauPrincipal">
          <div class="utd-container">
            <utd-menu-horizontal id="menuHorizontal" afficher-icone-accueil="true" :path-courant="pathCourant">
              <router-link to="/">Accueil</router-link>
              <utd-menu-horizontal-item libelle="Tableau de bord" href="/">
                <router-link to="/">Tableau de bord</router-link>
              </utd-menu-horizontal-item>
              <utd-menu-horizontal-item libelle="Instances" href="/instances">
                <router-link to="/instances">Instances</router-link>
              </utd-menu-horizontal-item>
              <utd-menu-horizontal-item libelle="Processus" href="/processus">
                <router-link to="/processus">Processus</router-link>
              </utd-menu-horizontal-item>
              <utd-menu-horizontal-item libelle="Concepteur" href="/concepteur">
                <router-link to="/concepteur">Concepteur</router-link>
              </utd-menu-horizontal-item>
            </utd-menu-horizontal>
          </div>
        </div>
      </header>

      <div class="utd-container">
        <main id="main">
          <router-view :key="$route.path" />
        </main>
      </div>
    </div>

    <utd-hautpage id="hautPage"></utd-hautpage>
    <footer class="utd">
      <utd-piv-pied-page id="pivPiedPage" :annee-copyright="anneeCourante">
        <div slot="liens">
          <ul>
            <li><router-link to="/accessibilite">Accessibilité</router-link></li>
          </ul>
        </div>
      </utd-piv-pied-page>
    </footer>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const pathCourant = ref('')
const anneeCourante = String(new Date().getFullYear())

// Le menu UTD identifie l'élément actif à partir du path : on le fixe après le rendu.
const majPath = (p: string): void => {
  const racine = '/' + (p.split('/')[1] ?? '')
  setTimeout(() => (pathCourant.value = racine === '/' ? '/' : racine))
}

onMounted(() => majPath(route.path))
watch(() => route.path, majPath)
</script>

<style>
.utd-conteneur-principal {
  min-height: calc(100vh - 233px);
}
</style>
