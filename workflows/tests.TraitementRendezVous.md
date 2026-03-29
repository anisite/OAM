# Tests — TraitementRendezVous

## Cas de test 1 : Rendez-vous trouvé et notification envoyée

### INPUT
```yaml
rendezVousId: "RDV-001"
```

### Mocks

#### rechercherRendezVousMock
```yaml
data:
  rendezVous:
    id: "RDV-001"
    date: "2026-04-15T10:00:00"
    client: "Jean Dupont"
    courriel: "jean.dupont@example.com"
```

#### notifierClientMock
```yaml
data:
  succes: true
  messageId: "MSG-12345"
```

### OUTPUT attendu
```yaml
etat: Termine
taches:
  rechercherRendezVous:
    etat: Reussie
  validerRendezVous:
    etat: Reussie
  notifierClient:
    etat: Reussie
  terminer:
    etat: Reussie
```

---

## Cas de test 2 : Rendez-vous introuvable — hook en attente

### INPUT
```yaml
rendezVousId: "RDV-INEXISTANT"
```

### Mocks

#### rechercherRendezVousMock
```yaml
data:
  rendezVous: null
```

### OUTPUT attendu
```yaml
etat: EnPause
taches:
  rechercherRendezVous:
    etat: Reussie
  validerRendezVous:
    etat: Reussie
  aucunRendezVous:
    etat: EnPause
```

---

## Cas de test 3 : Erreur HTTP sur la recherche

### INPUT
```yaml
rendezVousId: "RDV-ERREUR"
```

### Mocks

#### rechercherRendezVousMock (disabled)
_Mock désactivé — le connecteur HTTP échoue._

### OUTPUT attendu
```yaml
etat: EnErreur
taches:
  rechercherRendezVous:
    etat: EnErreur
```
