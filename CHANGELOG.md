# Changelog

What Mica shows in its update notice comes from here: the release workflow copies the section
matching the tag into the release body, and Mica displays the part written in the reader's
language. Keep both `### en` and `### fr` for every version — English is what Mica falls back to
when it has no section for the current language.

## 1.1.0

### en
- An app waiting in the notification area now hands back the memory it is not using —
  measured at around 300 MB of RAM for one app — and takes it back when you reopen it.
- The Mica window shows its own icon again; the installed copy was missing the file.
- Icons are decoded at the size they are drawn, pages are read only as far as they need to
  be, and an extension downloads to disk instead of into memory.

### fr
- Une app qui attend dans la zone de notification rend la mémoire qu'elle n'utilise pas —
  mesuré à environ 300 Mo de RAM pour une app — et la reprend à la réouverture.
- La fenêtre Mica affiche de nouveau son icône ; le fichier manquait à la copie installée.
- Les icônes sont décodées à la taille où elles sont dessinées, les pages ne sont lues que
  jusqu'où il faut, et une extension se télécharge sur le disque plutôt qu'en mémoire.

## 1.0.0

### en
- Export apps to one file and import them on another machine, extensions included. The file
  carries settings and extension ids, not their bytes, so it stays a few kilobytes.
- Double-click a .mica file to import it; it opens in the Mica you already have running.
- Sign-ins deliberately stay behind: Windows seals them to one account on one machine.
- The update notice shows the real changelog, in your language.
- Mica's own icon is legible again, and the window says which version this is.

### fr
- Exportez des apps dans un fichier et importez-les sur une autre machine, extensions comprises.
  Le fichier porte les réglages et les identifiants d'extensions, pas leurs octets : il reste de
  quelques kilo-octets.
- Un double-clic sur un fichier .mica l'importe ; il s'ouvre dans le Mica déjà lancé.
- Les connexions restent volontairement en arrière : Windows les scelle à un compte sur une
  machine.
- L'avis de mise à jour affiche le vrai changelog, dans votre langue.
- L'icône de Mica est de nouveau lisible, et la fenêtre indique de quelle version il s'agit.

## 0.5.0

Entries are one line each on purpose: Mica shows them in a notice, not a page.

### en
- Installed extensions appear in the toolbar and can be removed again — they were invisible.
- The store's "Add to Chrome" button is disabled; Mica installs the extension it names instead.
- An open app no longer overwrites settings you changed in Mica.
- Mica tells you when a newer version exists and installs it on request.

### fr
- Les extensions installées apparaissent dans la barre d'outils et peuvent en être retirées.
- Le bouton « Ajouter à Chrome » du store est désactivé ; Mica installe l'extension à sa place.
- Une app ouverte n'écrase plus les réglages modifiés dans Mica.
- Mica signale une version plus récente et l'installe sur demande.

## 0.4.0

### en
- Apps share one browser engine by default, which saves around 250 MB per extra app. Sessions
  stay separate; an app can be given its own engine from its settings.
- Settings for keeping an app running in the notification area and starting it with Windows.

### fr
- Les apps se partagent un moteur de navigation par défaut, ce qui économise environ 250 Mo par
  app supplémentaire. Les sessions restent séparées ; une app peut recevoir son propre moteur
  depuis ses réglages.
- Réglages pour garder une app dans la zone de notification et la lancer au démarrage de Windows.

## 0.3.0

### en
- Each app gets its own executable, so every shell shows its real name and icon.
- The interface is translated into 12 languages, with Arabic mirrored.

### fr
- Chaque app reçoit son propre exécutable, pour que tous les shells affichent son vrai nom et sa
  vraie icône.
- L'interface est traduite en 12 langues, l'arabe en lecture de droite à gauche.
