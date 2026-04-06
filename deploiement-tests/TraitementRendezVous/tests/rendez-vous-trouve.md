# Rendez-vous trouvé et notification envoyée

### INPUT
```yaml
rendezVousId: "RDV-001"
```

### Mocks

#### rechercherRendezVous
```yaml
data:
  rendezVous:
    id: "RDV-001"
    date: "2026-04-15T10:00:00"
    client: "Jean Dupont"
    courriel: "jean.dupont@example.com"
```

#### notifierClient
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
    input:
      courriel: "jean.dupont@example.com"
  terminer:
    etat: Reussie
```
