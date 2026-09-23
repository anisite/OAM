const fmtDateHeure = new Intl.DateTimeFormat('fr-CA', { dateStyle: 'short', timeStyle: 'medium' })
const fmtRelatif = new Intl.RelativeTimeFormat('fr-CA', { numeric: 'auto' })

export function dateHeure(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : fmtDateHeure.format(d)
}

export function relatif(iso?: string | null): string {
  if (!iso) return '—'
  const secondes = (new Date(iso).getTime() - Date.now()) / 1000
  const abs = Math.abs(secondes)
  if (abs < 60) return fmtRelatif.format(Math.round(secondes), 'second')
  if (abs < 3600) return fmtRelatif.format(Math.round(secondes / 60), 'minute')
  if (abs < 86400) return fmtRelatif.format(Math.round(secondes / 3600), 'hour')
  return fmtRelatif.format(Math.round(secondes / 86400), 'day')
}

export function duree(debut?: string, fin?: string): string {
  if (!debut) return '—'
  const ms = (fin ? new Date(fin).getTime() : Date.now()) - new Date(debut).getTime()
  const s = Math.round(ms / 1000)
  if (s < 60) return `${s} s`
  if (s < 3600) return `${Math.floor(s / 60)} min ${s % 60} s`
  if (s < 86400) return `${Math.floor(s / 3600)} h ${Math.floor((s % 3600) / 60)} min`
  return `${Math.floor(s / 86400)} j ${Math.floor((s % 86400) / 3600)} h`
}

/**
 * Les données DurableTask sont souvent du JSON encodé en chaîne, voire deux fois
 * (paramètres d'activité : ["{\"a\":1}"], résultats : "{\"a\":1}") : on les déballe pour l'affichage.
 */
function deballer(v: unknown, profondeur = 0): unknown {
  if (profondeur > 3) return v
  if (typeof v === 'string') {
    const t = v.trim()
    if (/^[[{"]/.test(t)) {
      try {
        return deballer(JSON.parse(t), profondeur + 1)
      } catch {
        return v
      }
    }
    return v
  }
  if (Array.isArray(v) && v.length === 1 && typeof v[0] === 'string' && /^\s*[[{]/.test(v[0])) return deballer(v[0], profondeur + 1)
  return v
}

export function json(valeur: unknown): string {
  if (valeur === undefined || valeur === null) return '—'
  const d = deballer(valeur)
  return typeof d === 'string' ? d : JSON.stringify(d, null, 2)
}

export const STATUTS: Record<string, { libelle: string; couleur: string }> = {
  Pending: { libelle: 'En file', couleur: 'gris' },
  Running: { libelle: 'En cours', couleur: 'bleu' },
  Suspended: { libelle: 'Suspendue', couleur: 'jaune' },
  Completed: { libelle: 'Terminée', couleur: 'vert' },
  Failed: { libelle: 'En échec', couleur: 'rouge' },
  Terminated: { libelle: 'Interrompue', couleur: 'rouge' },
  Canceled: { libelle: 'Annulée', couleur: 'gris' },
  ContinuedAsNew: { libelle: 'Relancée', couleur: 'bleu' }
}

export const STATUTS_ACTIFS = ['Pending', 'Running', 'Suspended']
