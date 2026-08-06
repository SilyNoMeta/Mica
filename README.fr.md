# Mica

*[English version](README.md)*

Transformez n'importe quel site en vraie app Windows. Vous donnez une adresse à Mica : il lit le
nom et l'icône publiés par le site, fabrique l'app et pose un raccourci dans le menu Démarrer.
Aucune étape de packaging, pas de mode développeur, rien à configurer.

L'app obtenue, c'est le site et rien d'autre : pas de barre d'adresse, pas de menus, aucune
extension imposée. <kbd>Alt</kbd> fait apparaître la barre d'outils quand vous en avez besoin,
et vous pouvez l'épingler pour qu'elle reste.

## Ce que vous obtenez

- **La vraie identité du site.** Le nom et l'icône viennent du manifeste web du site —
  l'étincelle de Gemini, la bulle de Messenger — rendus en 512 px puis écrits dans un `.ico`
  multi-résolutions. Mica ne redistribue aucun visuel tiers.
- **Une app à part, pas un onglet.** Son bouton dans la barre des tâches, son épinglage, son
  icône, sa ligne dans Paramètres › Applications installées.
- **Sa propre session connectée.** Chaque app a un profil WebView2 privé : deux apps du même
  domaine restent connectées à deux comptes différents, et les sessions survivent aux
  redémarrages.
- **Rien qui gêne.** Barre masquée jusqu'à <kbd>Alt</kbd>, épinglable. Les réglages se limitent
  volontairement à deux choses : le zoom et les extensions.
- **Les extensions du Chrome Web Store.** Le bouton « Ajouter à Chrome » du store ne peut rien
  installer dans un navigateur embarqué — il se dégrade en téléchargement qui échoue. Mica
  intercepte ce téléchargement et fait l'installation à sa place, et propose une bannière en un
  clic dès que vous arrivez sur la page d'une extension. Coller le lien dans les réglages
  fonctionne aussi. Un bloqueur de pub ? Installez celui que vous voulez — Mica ne choisit pas
  pour vous.
- **Les notifications web deviennent des toasts Windows**, attribués à l'app et non à un
  navigateur.
- **Les liens se comportent bien.** Un lien externe que vous cliquez s'ouvre dans votre vrai
  navigateur ; les redirections et les étapes de connexion restent dans l'app, pour qu'une
  authentification ne laisse jamais votre session ailleurs.

## Comment ça marche

Un seul exécutable hôte sert toutes les apps. Ce qui les fait se comporter comme des
applications distinctes, c'est l'**AppUserModelID** propre à chacune : Windows regroupe les
boutons de la barre des tâches, résout l'épinglage et attribue les toasts d'après cet
identifiant, pas d'après le chemin de l'exécutable. Mica l'inscrit à trois endroits, et les
trois sont nécessaires — sur le raccourci, sur la fenêtre avant son premier affichage (une
fenêtre sans identifiant retombe sur l'exécutable, et toutes les apps s'afficheraient alors
comme « Mica.Host »), et dans `HKCU\Software\Classes\AppUserModelId`, où le shell lit le nom et
l'icône.

Cela couvre la barre des tâches de Windows. Mais beaucoup de shells — les docks tiers en
particulier — ignorent complètement l'AppUserModelID et identifient une app en cours par son
**exécutable**, en affichant l'icône compilée dedans. Avec un hôte partagé unique, elles se
confondent toutes en une seule entrée sans nom. Chaque app reçoit donc aussi son propre
exécutable à côté de l'hôte partagé : une copie de `Mica.Host.exe` portant son nom, dont on
réécrit l'icône **et la ressource version**. L'icône seule ne suffit pas : ces shells lisent le
`FileDescription` pour le libellé, donc une copie qui conserve la ressource version de l'hôte
s'appelle encore « Mica.Host », quel que soit le nom du fichier.

La copie fonctionne parce qu'un apphost .NET stocke en interne le chemin de son assembly managé
au lieu de le déduire de son propre nom de fichier. WinUI, lui, cherche son index de ressources
sous `<nom de l'exécutable>.pri` : une copie sans cet index meurt dans `Microsoft.UI.Xaml` avant
d'atteindre le moindre code applicatif. Un lanceur est donc toujours une paire — `<slug>.exe` et
`<slug>.pri`, quelques centaines de kilo-octets. Les 40 Mo de runtime restent partagés.

Mica se place aussi *lui-même* dans le menu Démarrer à chaque lancement : une app non packagée
reste invisible pour Démarrer et la recherche tant qu'aucun raccourci n'existe. La même passe
repointe les raccourcis des apps vers l'installation courante, pour qu'un déplacement ou une
recompilation ne les laisse pas orphelins.

> Les lanceurs étant écrits à côté de l'hôte, Mica s'installe par utilisateur (le schéma retenu
> par Chrome et VS Code) plutôt que dans Program Files. Si le dossier de l'hôte n'est pas
> accessible en écriture, la création d'app se rabat sur l'exécutable partagé au lieu d'échouer.

```
%LOCALAPPDATA%\Mica\apps\<slug>\
├── app.json      nom, URL de départ, domaines internes, zoom, barre épinglée, taille fenêtre
├── icon.ico      l'icône du site de 16 à 256 px
├── icon.png      512 px, utilisée par la liste du builder
├── profile\      le profil WebView2 de cette app : cookies, stockage, connexion
├── extensions\   extensions décompressées installées depuis le Chrome Web Store
└── startup.log
```

```
<dossier d'installation>\<slug>.exe  +  <slug>.pri     l'exécutable propre à l'app
%APPDATA%\...\Menu Démarrer\Programmes\Mica\<Nom>.lnk  → <slug>.exe --app <slug>
HKCU\...\Uninstall\Mica.<slug>                         → Paramètres › Applications installées
```

Retirer une app supprime tout : raccourci, profil, extensions, entrée de registre.

## Organisation du dépôt

```
Mica.slnx
├── Mica.Core/            Tout ce que les deux apps partagent
│   ├── Shell/            BrowserShellPage — le contenu de la fenêtre d'une app générée
│   ├── Apps/             AppRegistry (créer/lister/retirer), ShortcutFactory (.lnk + AUMID),
│   │                     AppLauncherFactory + VersionResource (exécutable par app,
│   │                     ressources icône et version)
│   ├── Sites/            SiteInspector (nom + icône), IconPipeline (rendu → .ico), HostRules
│   ├── Extensions/       CrxInstaller — le chemin d'installation depuis le Chrome Web Store
│   ├── Integration/      Notifications, instance unique, journal de démarrage
│   ├── WebAppConfig.cs   La configuration d'une app
│   └── MicaPaths.cs      Où tout est rangé
├── Mica.Host/            L'exécutable partagé sur lequel tourne chaque app générée
└── Mica.Builder/         La fenêtre Mica : vos apps, et le flux « Ajouter une app »
```

## Build & exécution

Prérequis : .NET SDK 10, et le Windows App Runtime 2.3 (voir [Déploiement](#déploiement)).

```powershell
dotnet build Mica.slnx -p:Platform=x64
.\Mica.Builder\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Mica.Builder.exe
```

Les apps générées se lancent depuis leur raccourci, ou directement :

```powershell
.\Mica.Host\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Mica.Host.exe --app gemini
```

`--url <adresse>` exécute la coquille sur un site sans créer d'app — pratique pour jeter un œil
avant de se décider.

> Les raccourcis créés par un build de développement pointent dans `bin\`. Reconstruire ailleurs,
> ou supprimer le dossier, les laisse orphelins ; recréez l'app, ou retirez-la depuis
> Paramètres › Applications installées.

## Déploiement

Mica est **non packagé**, volontairement. Imposer le mode développeur et un enregistrement MSIX
par app irait contre l'idée même de créer des apps à la demande — une app est donc un dossier
plus un raccourci, et sa création est instantanée.

Il est aussi **framework-dependent** plutôt que self-contained, et c'est le seul vrai compromis
de la conception : un déploiement self-contained du Windows App SDK omet
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`, sans laquelle `AppNotificationManager` ne
peut pas s'enregistrer — le self-contained coûterait donc purement et simplement les
notifications web. L'installeur enchaînera l'installation du Windows App Runtime. En attendant,
installez-le vous-même :

```powershell
winget install Microsoft.WindowsAppRuntime.1.8
```

## Limites connues

- **Notifications uniquement app ouverte.** Pas de push en arrière-plan sans service dédié.
- **Le zoom est appliqué par la coquille, pas par le navigateur.** Le contrôle WebView2 de WinUI
  n'expose pas `ZoomFactor` : Mica met le document à l'échelle avec le `zoom` CSS de Chromium,
  qui refait la mise en page comme le zoom navigateur. <kbd>Ctrl</kbd> +/−/0 et
  <kbd>Ctrl</kbd>+molette y sont redirigés.
- **Les icônes SVG et WebP passent par Edge headless.** GDI+ ne sait décoder ni l'un ni l'autre,
  et la plupart des grands sites publient les deux aujourd'hui. Si Edge est indisponible, Mica
  se rabat sur le plus grand raster proposé par le site, puis sur un monogramme — la création
  d'une app n'échoue jamais à cause d'une icône.
- **Les extensions suivent les règles de WebView2.** Les versions de manifeste acceptées par
  votre runtime WebView2 sont celles que Mica accepte.
- Pas encore d'installeur, et pas encore d'interface en anglais — l'app est en français.

## Mentions légales

Code publié sous [licence MIT](LICENSE).

Mica ne redistribue aucune marque ni visuel tiers. Chaque app récupère le nom et l'icône publiés
par le site que l'utilisateur a choisi d'encapsuler, depuis ce site, au moment de la création.
Les extensions sont téléchargées depuis le service de mise à jour de Google à la demande
explicite de l'utilisateur et restent sous leurs propres licences. Gemini, Google, Messenger,
Facebook, Chrome et Windows sont des marques de leurs propriétaires respectifs ; ce projet n'est
ni affilié, ni approuvé, ni sponsorisé par aucun d'eux.
