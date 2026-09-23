declare let utd: any

/**
 * Enrobage des API de feedback UTD (utd.message, utd.notification).
 * Même approche que dans GED afin de garder un comportement uniforme.
 */
export default class MessagesHelpers {
  static afficherErreurTechnique(
    message?: string | null,
    titre?: string | null,
    idControleFocusFermeture?: string | null
  ): void {
    titre = titre || 'Erreur'
    message =
      message ||
      `<p>Une erreur technique est survenue.</p><p>Veuillez réessayer et si le problème persiste, veuillez vous référer à votre répondant local ou régional.</p>`

    utd.message.afficher({
      type: 'erreur',
      titre: titre,
      corps: message,
      texteBoutonPrimaire: 'Ok',
      idControleFocusFermeture: idControleFocusFermeture
    })
  }

  static afficherAvertissement(
    message: string,
    titre?: string,
    idControleFocusFermeture?: string | null
  ): void {
    utd.message.afficher({
      type: 'avertissement',
      titre: titre || 'Avertissement',
      corps: message,
      texteBoutonPrimaire: 'Ok',
      idControleFocusFermeture: idControleFocusFermeture
    })
  }

  /** Demande de confirmation. Retourne true si le bouton primaire a été cliqué. */
  static async confirmer(
    message: string,
    titre = 'Confirmation',
    texteBoutonPrimaire = 'Confirmer',
    texteBoutonSecondaire = 'Annuler'
  ): Promise<boolean> {
    const retour = await utd.message.afficher({
      type: 'avertissement',
      titre,
      corps: message,
      texteBoutonPrimaire,
      texteBoutonSecondaire
    })

    return retour === 'primaire'
  }

  static notifierSucces(message: string, titre?: string): void {
    utd.notification.emettre({ type: 'positif', titre, message })
  }

  static notifierErreur(message: string, titre = 'Erreur'): void {
    utd.notification.emettre({ type: 'negatif', titre, message })
  }

  static notifierInfo(message: string, titre?: string): void {
    utd.notification.emettre({ type: 'neutre', titre, message })
  }

  /** Notification hors écran pour les lecteurs d'écran. */
  static notifierLecteurEcran(texte: string): void {
    const idZoneNotification = 'zoneNotificationLecteurEcran'

    let zone = document.getElementById(idZoneNotification)
    if (!zone) {
      zone = document.createElement('div')
      zone.id = idZoneNotification
      zone.setAttribute('role', 'status')
      zone.classList.add('utd-sr-only')
      document.body.appendChild(zone)

      // setTimeout nécessaire pour le lecteur d'écran, sinon rien n'est lu la
      // première fois (le contrôle vient d'être ajouté au DOM).
      setTimeout(() => {
        if (zone) {
          zone.innerHTML = texte
          this.viderContenuNotificationLecteurEcran(idZoneNotification)
        }
      }, 200)
    } else {
      zone.innerHTML = texte
      this.viderContenuNotificationLecteurEcran(idZoneNotification)
    }
  }

  private static viderContenuNotificationLecteurEcran(id: string): void {
    setTimeout(() => {
      const zone = document.getElementById(id)
      if (zone) {
        zone.innerHTML = ''
      }
    }, 3000)
  }
}
