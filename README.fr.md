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
├── Mica.Builder/         La fenêtre Mica : vos apps, et le flux « Ajouter une app »
├── installer/            Publish.ps1 (charge utile) et Mica.iss (Inno Setup)
└── tools/                Generate-MicaIcon.ps1 — la marque de Mica
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

## Langues

Mica est livré en 12 langues et suit celle de Windows : anglais, français, espagnol, portugais
(Brésil), allemand, italien, russe, chinois simplifié, japonais, coréen, vietnamien et arabe.
L'anglais sert de repli quand Windows tourne dans une autre langue. `--lang <tag>` en force une,
pour qui ne veut pas lire l'app dans la langue de son système.

L'arabe met l'interface en miroir : la mise en page bascule via `FlowDirection`, piloté par une
entrée `FlowDirection` du catalogue plutôt que par une liste de langues codée en dur, et les
flèches précédent/suivant s'inversent avec `MirroredWhenRightToLeft`. **Le contenu web n'est
jamais mis en miroir** : la page décide de sa propre direction, et la forcer retournerait des
mises en page déjà correctes.

Toutes les chaînes vivent dans [`tools/strings.host.json`](tools/strings.host.json) et
[`tools/strings.builder.json`](tools/strings.builder.json), une entrée par clé avec toutes les
langues à côté. Ajouter une langue, c'est un tag par entrée, pas un fichier à écrire à la main :

```powershell
powershell -ExecutionPolicy Bypass -File tools\Generate-Resources.ps1
```

Le générateur refuse de produire quoi que ce soit s'il manque une traduction : le trou apparaît
au build au lieu de partir en production sous forme de libellé resté en anglais. `-Check` valide
sans rien écrire.

Deux choses à savoir avant d'y toucher, parce que les deux échouent en silence :

- **Le `.resw` d'une bibliothèque est inatteignable à l'exécution en non packagé** — ni via
  `x:Uid`, ni via aucune resource map. En revanche le code d'une bibliothèque lit sans problème
  les ressources de l'**exécutable** : toutes les chaînes vivent donc dans `Mica.Host` et
  `Mica.Builder`, et la coquille demande ses mots à son hôte. C'est pourquoi rien n'est localisé
  par `x:Uid`.
- **Un point dans une clé `.resw` signifie « élément point propriété »** — `Meta.FlowDirection`
  est compilé en `Meta/FlowDirection` et ne se résout jamais à plat. Les clés ici sont sans point.

## Installateur

```powershell
powershell -ExecutionPolicy Bypass -File installer\Publish.ps1
iscc installer\Mica.iss /DAppVersion=0.1.0
```

Cela produit `dist\MicaSetup-<version>-x64.exe`, environ 54 Mo. Il installe par utilisateur dans
`%LOCALAPPDATA%\Programs\Mica`, **sans droits administrateur ni mode développeur**. Le choix du
par-utilisateur n'est pas de la politesse : Mica écrit un lanceur dans son propre dossier
d'installation pour chaque app créée, ce dossier doit donc rester inscriptible par vous.

La désinstallation demande si vous voulez supprimer les apps créées, et propose par défaut de
les garder — les supprimer efface leurs profils de navigation, donc vous déconnecte de tous les
sites qu'elles ouvrent.

Poser un tag `v*` déclenche [`.github/workflows/release.yml`](.github/workflows/release.yml),
qui publie, compile l'installateur et l'attache à une release en brouillon.

### Prérequis d'exécution

Mica est **non packagé**, volontairement. Imposer le mode développeur et un enregistrement MSIX
par app irait contre l'idée même de créer des apps à la demande — une app est donc un dossier
plus un raccourci, et sa création est instantanée.

.NET est embarqué en **self-contained** : aucun runtime .NET n'est requis. Le Windows App SDK,
lui, ne l'est délibérément pas : un déploiement self-contained du SDK omet
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`, sans laquelle `AppNotificationManager` ne
peut pas s'enregistrer — le self-contained coûterait purement et simplement les notifications
web. L'installateur détecte le Windows App Runtime et le télécharge s'il manque ; ce
téléchargement est le prix à payer pour garder les notifications. Pour l'installer à la main :

```powershell
winget install Microsoft.WindowsAppRuntime.2
```

> `dotnet publish` n'émet pas le `<nom>.pri` propre à une app WinUI non packagée, seulement ceux
> de ses bibliothèques. Un build publié sans ce fichier meurt dans `Microsoft.UI.Xaml` avant
> d'atteindre le moindre code applicatif : `Publish.ps1` récupère donc l'index de ressources de
> chaque exécutable depuis la sortie de build — et Mica a de toute façon besoin de celui de
> l'hôte, pour en donner une copie à chaque lanceur qu'il écrit.

## Limites connues

- **Les notifications exigent que l'app tourne.** « Garder ouverte à la fermeture » répond au
  besoin en laissant la page chargée en arrière-plan ; il n'y a toujours pas de push quand l'app
  est vraiment quittée, ce qui demanderait un service. Chaque app en arrière-plan garde son propre
  moteur de rendu, environ 150–300 Mo.
- **Un lanceur embarque une copie de l'index de ressources de l'hôte** : après une mise à jour de
  Mica, une app plus ancienne afficherait des clés brutes. Chaque app rafraîchit cette copie au
  lancement — la réparation intervient donc au démarrage suivant, pas instantanément.
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
- **Les pluriels n'ont que deux formes** — un compteur affiche soit « 1 app », soit la forme
  plurielle. Les langues à duel ou paucal (arabe, russe, polonais) reçoivent la forme plurielle
  pour tout compte supérieur à un.

## Mentions légales

Code publié sous [licence MIT](LICENSE).

Mica ne redistribue aucune marque ni visuel tiers. Chaque app récupère le nom et l'icône publiés
par le site que l'utilisateur a choisi d'encapsuler, depuis ce site, au moment de la création.
Les extensions sont téléchargées depuis le service de mise à jour de Google à la demande
explicite de l'utilisateur et restent sous leurs propres licences. Gemini, Google, Messenger,
Facebook, Chrome et Windows sont des marques de leurs propriétaires respectifs ; ce projet n'est
ni affilié, ni approuvé, ni sponsorisé par aucun d'eux.
