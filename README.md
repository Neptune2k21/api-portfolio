# api-portfolio

API pour mon portfolio développée en ASP.NET Core 10.

Elle gère les données musicales du portfolio à partir de Last.fm : dernières écoutes, statistiques, artistes, albums et activité musicale.

Les images manquantes sont récupérées depuis Deezer.

Elle sert aussi les articles, notes et journal écrits en markdown dans [contenu-portfolio](https://github.com/Neptune2k21/contenu-portfolio), lus par l'API GraphQL de GitHub et rechargés à chaque push via un webhook.

**Technologies :** ASP.NET Core 10, Minimal APIs, Native AOT.

**Fonctionnalités :**
- API sécurisée par clé d'accès.
- Cache des données et des images.
- Gestion des appels à Last.fm et Deezer.
- Articles depuis un dépôt Git : brouillons, publication programmée, historique des modifications, lecture d'une copie locale pour écrire sans pousser.
- Gestion des erreurs et des indisponibilités.
- Tests unitaires et d'intégration.