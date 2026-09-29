# Code signing policy

## État

Préparation NIN-9, **sans compte SignPath ni projet approuvé**. Aucun certificat de confiance
n'a été obtenu et aucun artefact signé n'a été produit par cette intégration.
Le workflow `package.yml` reste un build de vérification non signé.

La licence du code DockPad est MIT, copyright 2026 syl-craft. Les composants tiers restent
sous leur propre licence. La signature ne change pas leur titularité.

## Responsabilités prévues

- Auteur et mainteneur : [syl-craft](https://github.com/syl-craft).
- Revue des contributions externes et approbation des demandes de signature : syl-craft.
- Chaque demande de signature de production nécessite une approbation humaine dans SignPath.
- Les builds soumis proviennent de runners GitHub hébergés, depuis la branche `master`
  revue. Le workflow de préparation ne publie pas de release.

Ces rôles sont à confirmer pendant l'inscription. Activer l'authentification multifacteur
sur les comptes mainteneurs GitHub et SignPath conformément aux conditions du programme.
Après admission effective seulement, ajouter sur la page de téléchargement la mention :
« Free code signing provided by SignPath.io, certificate by SignPath Foundation ».

## Informations réseau

DockPad stocke ses réglages dans le profil local. Il peut contacter GitHub pour rechercher
et télécharger les mises à jour ; la recherche automatique est désactivable dans Mises à jour.
Le téléchargement automatique des favicons, lorsqu'il est activé, contacte des services
Google S2 à partir des domaines des raccourcis ([confidentialité Google](https://policies.google.com/privacy)). Les liens ouverts, navigateurs, outils MCP,
fournisseurs d'usage IA et gestionnaires de secrets sont ceux configurés par l'utilisateur.
Il faut décrire les flux et services effectivement activés dans la candidature ; ne pas
déclarer une absence totale de trafic réseau automatique. Les politiques de confidentialité
des services concernés restent applicables, notamment [GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Première activation SignPath

1. Créer le compte et déposer la [candidature Foundation](https://signpath.org/apply.html)
   pour le dépôt public `https://github.com/syl-craft/DockPad`, après publication de la licence
   et de cette politique. L'admission est une décision de SignPath.
2. Faire examiner explicitement les trois exécutables Velopack construits depuis les sources
   épinglées : `setup`, `update` et le lanceur adapté. Notre patch évite la fermeture forcée
   des sessions MCP. Les conditions Foundation excluent la signature indiscriminée de
   binaires tiers : obtenir une décision sur ces composants avant d'automatiser leur signature.
3. Configurer le projet SignPath, le connecteur GitHub, la politique avec approbation humaine
   et la configuration d'artefact `tools/signing/signpath-application.xml`.
4. Créer l'environnement GitHub `signpath`, avec revue obligatoire et accès limité à `master`.
   Ajouter le secret `SIGNPATH_API_TOKEN` via l'interface GitHub, jamais dans le dépôt ni le chat.
   Ajouter les variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
   `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_APPLICATION_CONFIGURATION_SLUG` et
   `SIGNPATH_CERTIFICATE_THUMBPRINT` (empreinte publique du certificat attendu).
5. Lancer manuellement `signpath-onboarding.yml` après merge. Il teste et compile depuis les
   sources, transmet l'artefact GitHub à SignPath puis contrôle les signatures, horodatages,
   certificat, produit et version retournés. Les DLL tierces ne sont pas re-signées.

Ce premier workflow signe **l'application uniquement**. Son résultat n'est pas une release
Velopack, ne doit pas être publié comme Setup, et ne débloque pas à lui seul NIN-8.

## Intégration de la release Velopack après admission

Le point d'entrée `tools/velopack/pack.ps1 -SignTemplate` existe, mais **l'adaptateur SignPath
pour le packaging complet reste à finaliser et à exécuter** avec la politique approuvée.
Ne pas brancher un simple envoi REST de fichiers locaux à la place du connecteur de provenance.

L'ordre est essentiel : Velopack modifie les ressources du lanceur et de l'updater avant de
les signer, construit les paquets à partir des fichiers signés, puis signe le Setup final.
Signer seulement le Setup laisse ses composants internes non signés ; re-signer des DLL dans
un paquet déjà publié invalide ses hashes et deltas. Les étapes du futur adaptateur doivent
respecter cet ordre et conserver la provenance GitHub exigée par SignPath.

Une release destinée à la diffusion devra employer :

```powershell
./tools/velopack/pack.ps1 -RequireSigned -CertificateThumbprint <empreinte-publique> -SignTemplate <commande-approuvee>
```

`-RequireSigned` échoue si la commande ou l'empreinte manque, puis vérifie les deux noms du
Setup, le lanceur portable, l'updater, les cinq binaires DockPad dans le portable et le paquet
complet, ainsi que les hashes du feed. Le générateur WinGet calcule ensuite le SHA256 du Setup
final. Valider sur une VM Windows propre l'installation, la mise à jour intégrée et WinGet.
Une signature Authenticode valide ne promet pas l'absence de tout avertissement SmartScreen.

## Critères de clôture NIN-9

- Admission Foundation et périmètre des binaires natifs confirmés.
- Configuration effective des comptes, approbateurs, certificat et provenance GitHub.
- Adaptateur de signature intégré aux étapes de packaging Velopack, sans re-signer les tiers.
- Build réel signé et horodaté, vérification du certificat et cohérence du feed réussies.
- Installation et mise à niveau du Setup signé vérifiées sur Windows propre.

## Vérification locale de la préparation

```powershell
dotnet publish DockPad.csproj -c Release -r win-x64 --self-contained false -p:SkipLegacyZip=true -p:IncludeSourceRevisionInInformationalVersion=false -p:Version=1.24.0 -o .tools/signing-validation
Import-Module Pester -RequiredVersion 5.7.1
Invoke-Pester -Path tools/signing/Signing.Tests.ps1 -Output Detailed
```

Ces tests emploient de vrais fichiers PE publiés et de vraies archives, mais simulent les
réponses Authenticode pour exercer les refus (mauvais certificat, absence d'horodatage,
version incorrecte, updater non signé, feed incohérent). Ils vérifient aussi le refus des
vrais binaires non signés. Ils ne constituent **pas** la preuve d'une signature SignPath réelle.

Références : [conditions Foundation](https://signpath.org/terms.html),
[intégration GitHub](https://docs.signpath.io/trusted-build-systems/github),
[configuration des artefacts](https://docs.signpath.io/artifact-configuration/reference),
[signature Velopack](https://docs.velopack.io/packaging/signing).
