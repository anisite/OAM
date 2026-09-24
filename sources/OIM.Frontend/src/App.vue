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
            <!-- Recréé à chaque changement d'équipe : le menu UTD lit ses éléments à la création. -->
            <utd-menu-horizontal id="menuHorizontal" :key="equipe" afficher-icone-accueil="true" :path-courant="pathCourant">
              <router-link to="/">Accueil</router-link>
              <template v-if="equipe">
                <utd-menu-horizontal-item libelle="Tableau de bord" :href="`/${equipe}`">
                  <router-link :to="`/${equipe}`">Tableau de bord</router-link>
                </utd-menu-horizontal-item>
                <utd-menu-horizontal-item libelle="Instances" :href="`/${equipe}/instances`">
                  <router-link :to="`/${equipe}/instances`">Instances</router-link>
                </utd-menu-horizontal-item>
                <utd-menu-horizontal-item libelle="Processus" :href="`/${equipe}/processus`">
                  <router-link :to="`/${equipe}/processus`">Processus</router-link>
                </utd-menu-horizontal-item>
                <utd-menu-horizontal-item libelle="Concepteur" :href="`/${equipe}/concepteur`">
                  <router-link :to="`/${equipe}/concepteur`">Concepteur</router-link>
                </utd-menu-horizontal-item>
              </template>
              <utd-menu-horizontal-item v-else libelle="Équipes" href="/">
                <router-link to="/">Équipes</router-link>
              </utd-menu-horizontal-item>
              <utd-menu-horizontal-item v-if="moi?.admin" libelle="Administration" href="/admin/equipes">
                <router-link to="/admin/equipes">Administration</router-link>
              </utd-menu-horizontal-item>
            </utd-menu-horizontal>
          </div>
        </div>
      </header>

      <div class="utd-container">
        <main id="main">
          <!-- Espace d'équipe : conservé tant que l'équipe ne change pas (ses pages ont leur propre clé). -->
          <router-view :key="equipe || $route.path" />
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
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { chargerMoi, moi } from '@/lib/session'

const route = useRoute()
const pathCourant = ref('')
const anneeCourante = String(new Date().getFullYear())

/** Équipe de l'URL (/:equipe/...), sinon vide (accueil, administration). */
const equipe = computed(() => (typeof route.params.equipe === 'string' ? encodeURIComponent(route.params.equipe) : ''))

// Le menu UTD identifie l'élément actif à partir du path : on le fixe après le rendu
// (« /equipe/instances/x » → « /equipe/instances »).
const majPath = (p: string): void => {
  const segments = p.split('/').filter(Boolean)
  const racine = segments[0] === 'admin' ? '/admin/equipes' : '/' + segments.slice(0, equipe.value ? 2 : 1).join('/')
  setTimeout(() => (pathCourant.value = racine))
}

chargerMoi().catch(() => undefined)

onMounted(() => majPath(route.path))
watch(() => route.path, majPath)
</script>

<style>
.utd-conteneur-principal {
  min-height: calc(100vh - 233px);
}
</style>
