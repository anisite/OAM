<template>
  <div>
    <div class="entete-page">
      <h1>Équipes</h1>
      <button v-if="moi?.admin" type="button" class="utd-btn primaire compact" @click="ouvrir(null)">Nouvelle équipe</button>
    </div>

    <utd-avis v-if="moi && !moi.admin" type="avertissement" titre="Réservé aux administrateurs OIM.">
      <p><router-link to="/">Retour au choix de l’équipe</router-link></p>
    </utd-avis>

    <div v-else-if="equipes" class="carte">
      <p class="texte-attenue">
        Les membres sont des <em>sujets</em> de jeton : compte ou groupe AD (<span class="texte-mono">DOMAINE\nom</span>), ou groupe
        d’un autre fournisseur de jetons avec son préfixe.
      </p>
      <p v-if="equipes.length === 0" class="zone-vide">Aucune équipe.</p>
      <table v-else class="utd-table bordures-lignes hover-lignes compact">
        <caption class="utd-sr-only">Équipes OIM</caption>
        <thead>
          <tr>
            <th scope="col">Équipe</th>
            <th scope="col">État</th>
            <th scope="col" class="colonne-nombre">Actives</th>
            <th scope="col" class="colonne-nombre">En échec</th>
            <th scope="col"><span class="utd-sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="e in equipes" :key="e.id">
            <td>
              <router-link :to="`/${encodeURIComponent(e.id)}`"><strong>{{ e.nom }}</strong></router-link>
              <div class="texte-attenue texte-mono">{{ e.id }}</div>
              <div v-if="e.description" class="texte-attenue">{{ e.description }}</div>
            </td>
            <td><span class="utd-pastille" :class="e.actif ? 'vert' : 'gris'">{{ e.actif ? 'Active' : 'Désactivée' }}</span></td>
            <td class="colonne-nombre">{{ e.compteurs.actives }}</td>
            <td class="colonne-nombre">{{ e.compteurs.echecs }}</td>
            <td class="cellule-actions">
              <button type="button" class="utd-btn secondaire compact" @click="ouvrir(e.id)">Modifier</button>
              <button type="button" class="utd-btn tertiaire comme-lien compact" @click="supprimer(e)">Supprimer</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <Dialogue id="dialogueEquipe" :titre="edition.id ? `Modifier « ${edition.nom} »` : 'Nouvelle équipe'" :visible="dialogueVisible"
      @update:visible="dialogueVisible = $event">
      <utd-avis v-if="erreur" type="erreur" :titre="erreur"></utd-avis>

      <utd-champ-form v-if="!edition.existante" id="champIdEquipe" libelle="Identifiant" obligatoire="true"
        precision="Dans les adresses et les identifiants : minuscules, chiffres et tirets (ex. sgd-prestations). Non modifiable." format="md">
        <input v-model="edition.id" type="text" class="texte-mono" maxlength="50" />
      </utd-champ-form>
      <utd-champ-form id="champNomEquipe" libelle="Nom" obligatoire="true" format="lg">
        <input v-model="edition.nom" type="text" maxlength="200" />
      </utd-champ-form>
      <utd-champ-form id="champDescriptionEquipe" libelle="Description" format="lg">
        <textarea v-model="edition.description" rows="2" maxlength="1000"></textarea>
      </utd-champ-form>
      <utd-champ-form id="champMembresEquipe" libelle="Membres" precision="Un sujet par ligne (ex. MES\gr_equipe_sgd, MES\cotda05)." format="lg">
        <textarea v-model="edition.membres" rows="6" class="texte-mono"></textarea>
      </utd-champ-form>
      <label v-if="edition.existante" class="interrupteur">
        <input v-model="edition.actif" type="checkbox" />
        Équipe active (désactivée : ses membres n’y ont plus accès et rien ne peut y être démarré)
      </label>

      <template #pied>
        <button type="button" class="utd-btn secondaire compact" @click="dialogueVisible = false">Annuler</button>
        <button type="button" class="utd-btn primaire compact" :disabled="envoi" @click="enregistrer">Enregistrer</button>
      </template>
    </Dialogue>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import Dialogue from '@/components/Dialogue.vue'
import { api } from '@/lib/api'
import { useFilAriane } from '@/lib/filAriane'
import { chargerMoi, moi } from '@/lib/session'
import type { ResumeEquipe } from '@/lib/types'
import MessagesHelpers from '@/helpers/messages'

useFilAriane(() => [{ libelle: 'Accueil', lien: '/' }, { libelle: 'Administration des équipes' }])

const equipes = ref<ResumeEquipe[] | null>(null)
const dialogueVisible = ref(false)
const envoi = ref(false)
const erreur = ref('')
const edition = reactive({ existante: false, id: '', nom: '', description: '', membres: '', actif: true })

const charger = async (): Promise<void> => {
  equipes.value = await api.equipes()
}

onMounted(async () => {
  if ((await chargerMoi()).admin) await charger()
})

const lignes = (texte: string): string[] => texte.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)

const ouvrir = async (id: string | null): Promise<void> => {
  erreur.value = ''
  if (id) {
    const e = await api.equipe(id)
    Object.assign(edition, { existante: true, id: e.id, nom: e.nom, description: e.description ?? '', membres: e.membres.join('\n'), actif: e.actif })
  } else {
    Object.assign(edition, { existante: false, id: '', nom: '', description: '', membres: '', actif: true })
  }
  dialogueVisible.value = true
}

const enregistrer = async (): Promise<void> => {
  envoi.value = true
  erreur.value = ''
  try {
    const description = edition.description || undefined
    if (edition.existante) {
      await api.modifierEquipe(edition.id, { nom: edition.nom, description, actif: edition.actif })
      await api.definirMembres(edition.id, lignes(edition.membres))
    } else {
      await api.creerEquipe({ id: edition.id.trim(), nom: edition.nom, description, membres: lignes(edition.membres) })
    }
    MessagesHelpers.notifierSucces(`Équipe « ${edition.nom} » enregistrée.`)
    dialogueVisible.value = false
    await charger()
  } catch (e) {
    erreur.value = (e as Error).message
  } finally {
    envoi.value = false
  }
}

const supprimer = async (e: ResumeEquipe): Promise<void> => {
  const ok = await MessagesHelpers.confirmer(
    `<p>Supprimer l’équipe « ${e.nom} » et la liste de ses membres ? Une équipe qui a encore des processus ne peut pas être supprimée : désactivez-la plutôt.</p>`,
    'Supprimer l’équipe',
    'Supprimer'
  )
  if (!ok) return
  try {
    await api.supprimerEquipe(e.id)
    MessagesHelpers.notifierSucces(`Équipe « ${e.nom} » supprimée.`)
    await charger()
  } catch (err) {
    MessagesHelpers.afficherErreurTechnique(`<p>${(err as Error).message}</p>`, 'Suppression impossible')
  }
}
</script>
