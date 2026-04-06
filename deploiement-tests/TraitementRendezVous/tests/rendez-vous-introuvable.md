# Rendez-vous introuvable — hook en attente

### INPUT
```yaml
rendezVousId: "RDV-INEXISTANT"
```

### Mocks

#### rechercherRendezVous
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
