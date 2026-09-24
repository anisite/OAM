/**
 * Jeton Bearer de l'API : obtenu par authentification Windows (/api/auth/jeton), gardé en mémoire
 * seulement et renouvelé une minute avant son expiration. En développement (proxy Vite, qui ne relaie
 * pas la négociation Windows), /api/auth/jeton-dev.
 */

interface JetonEmis {
  jeton: string | null
  expiration: string | null
  utilisateur: string
  roles: string[]
}

let jeton: string | null = null
let expiration = 0
let enCours: Promise<void> | null = null

async function obtenir(): Promise<void> {
  const url = import.meta.env.DEV ? '/api/auth/jeton-dev' : '/api/auth/jeton'
  let reponse = await fetch(url, { credentials: 'include' })
  // jeton-dev n'existe pas si la sécurité est désactivée : /api/auth/jeton répond alors sans jeton.
  if (reponse.status === 404 && import.meta.env.DEV) reponse = await fetch('/api/auth/jeton', { credentials: 'include' })
  if (!reponse.ok) {
    const json = await reponse.json().catch(() => undefined)
    throw new Error(json?.title ?? `Authentification refusée (${reponse.status}).`)
  }
  const emis = (await reponse.json()) as JetonEmis
  jeton = emis.jeton
  expiration = emis.expiration ? Date.parse(emis.expiration) : Number.POSITIVE_INFINITY
}

/** En-tête Authorization à joindre aux appels (vide si la sécurité est désactivée). */
export async function enteteAutorisation(forcer = false): Promise<Record<string, string>> {
  if (forcer || expiration - Date.now() < 60_000) {
    enCours ??= obtenir().finally(() => (enCours = null))
    await enCours
  }
  return jeton ? { Authorization: `Bearer ${jeton}` } : {}
}
