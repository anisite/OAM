import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem('oam_token'))
  const utilisateur = ref(localStorage.getItem('oam_utilisateur'))

  async function initialiser() {
    if (!token.value) {
      await obtenirToken()
    }
  }

  async function obtenirToken() {
    try {
      // En dev, utiliser dev-token (pas de NTLM) ; en prod, NTLM via /api/auth/token
      const url = import.meta.env.DEV ? '/api/auth/dev-token' : '/api/auth/token'
      const response = await fetch(url, { credentials: 'include' })
      if (response.ok) {
        const data = await response.json()
        token.value = data.token
        utilisateur.value = data.utilisateur
        localStorage.setItem('oam_token', data.token)
        localStorage.setItem('oam_utilisateur', data.utilisateur)
      }
    } catch (err) {
      console.error('Erreur authentification:', err)
    }
  }

  function deconnecter() {
    token.value = null
    utilisateur.value = null
    localStorage.removeItem('oam_token')
    localStorage.removeItem('oam_utilisateur')
  }

  function headers() {
    return token.value ? { Authorization: `Bearer ${token.value}` } : {}
  }

  return { token, utilisateur, initialiser, obtenirToken, deconnecter, headers }
})
