<template>
  <div>
    <div class="entete-page">
      <h1>Processus</h1>
      <div class="barre-actions">
        <input ref="champFichier" type="file" accept=".zip,.yml,.yaml" class="utd-d-none" aria-label="Paquet à déployer" @change="importer" />
        <button type="button" class="utd-btn secondaire compact" @click="champFichier?.click()">Déployer un fichier (.zip ou .yml)</button>
        <router-link :to="lienConcepteur()" class="utd-btn primaire compact">Nouveau processus</router-link>
      </div>
    </div>

    <utd-avis v-if="diagnostics.length" type="erreur" titre="Le déploiement a été refusé.">
      <ul>
        <li v-for="(d, i) in diagnostics" :key="i">
          <strong v-if="d.etape">[{{ d.etape }}]</strong> {{ d.message }}
        </li>
      </ul>
    </utd-avis>

    <div class="carte">
      <p v-if="definitions && definitions.length === 0" class="zone-vide">
        Aucun processus déployé. Créez-en un avec le <router-link :to="lienConcepteur()">concepteur</router-link>
        ou déposez un paquet (.zip contenant le YAML et ses gabarits).
      </p>
      <table v-else class="utd-table bordures-lignes hover-lignes tableau-cliquable">
        <caption class="utd-sr-only">Processus déployés</caption>
        <thead>
          <tr>
            <th scope="col">Processus</th>
            <th scope="col">Version</th>
            <th scope="col">État</th>
            <th scope="col">Dernier déploiement</th>
            <th scope="col"><span class="utd-sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="d in definitions ?? []" :key="d.id" @click="router.push(lienProcessus(d.id))">
            <td>
              <router-link :to="lienProcessus(d.id)" @click.stop><strong>{{ d.nom ?? idLocal(d.id) }}</strong></router-link>
              <div class="texte-attenue texte-mono">{{ idLocal(d.id) }}</div>
              <div v-if="d.description" class="texte-attenue">{{ d.description }}</div>
            </td>
            <td>v{{ d.versionCourante }} <span class="texte-attenue">({{ d.nbVersions }} version(s))</span></td>
            <td>
              <span class="utd-pastille" :class="d.actif ? 'vert' : 'gris'">{{ d.actif ? 'Actif' : 'Désactivé' }}</span>
            </td>
            <td>{{ dateHeure(d.modifieLe) }}<div class="texte-attenue">{{ d.deployePar }}</div></td>
            <td class="cellule-actions" @click.stop>
              <router-link :to="lienConcepteur(d.id)" class="utd-btn secondaire compact">Modifier</router-link>
              <router-link :to="{ path: lien('/instances'), query: { processus: d.id } }" class="utd-btn tertiaire comme-lien compact">Instances</router-link>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ErreurApi } from '@/lib/api'
import { idLocal, useEquipe } from '@/lib/equipe'
import { dateHeure } from '@/lib/format'
import type { Diagnostic, ResumeDefinition } from '@/lib/types'
import MessagesHelpers from '@/helpers/messages'

const router = useRouter()
const { equipe, lien, lienProcessus, lienConcepteur } = useEquipe()
const definitions = ref<ResumeDefinition[] | null>(null)
const diagnostics = ref<Diagnostic[]>([])
const champFichier = ref<HTMLInputElement | null>(null)

const charger = async (): Promise<void> => {
  definitions.value = await api.definitions(equipe.value)
}
onMounted(charger)


const importer = async (ev: Event): Promise<void> => {
  const input = ev.target as HTMLInputElement
  const fichier = input.files?.[0]
  input.value = ''
  if (!fichier) return
  diagnostics.value = []
  try {
    const r = fichier.name.endsWith('.zip')
      ? await api.deployerZip(equipe.value, fichier)
      : await api.deployer(equipe.value, await fichier.text(), {}, fichier.name)
    MessagesHelpers.notifierSucces(
      r.nouvelleVersion ? `« ${idLocal(r.id)} » v${r.version} déployé.` : `« ${idLocal(r.id)} » est inchangé (v${r.version}).`,
      'Déploiement'
    )
    await charger()
  } catch (e) {
    if (e instanceof ErreurApi && e.corps?.diagnostics) diagnostics.value = e.corps.diagnostics.filter((d: Diagnostic) => d.gravite === 'erreur')
    else MessagesHelpers.afficherErreurTechnique(`<p>${(e as Error).message}</p>`, 'Déploiement impossible')
  }
}
</script>
