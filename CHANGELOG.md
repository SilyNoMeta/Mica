# Changelog

What Mica shows in its update notice comes from here: the release workflow copies the section
matching the tag into the release body, and Mica displays the part written in the reader's
language. Keep both `### en` and `### fr` for every version — English is what Mica falls back to
when it has no section for the current language.

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
