# Mica

*[Version française](README.fr.md)*

Turn any website into a real Windows app. You give Mica an address; it reads the site's own
name and icon, writes an app, and drops a shortcut in your Start menu. No packaging step, no
Developer Mode, nothing to configure.

The generated app is the site and nothing else: no address bar, no menus, no bundled
extensions. Press <kbd>Alt</kbd> when you need the toolbar, pin it if you want it to stay.

## What you get

- **The site's real identity.** The name and icon come from the site's own web manifest —
  Gemini's sparkle, Messenger's bubble — rendered at 512 px and written into a
  multi-resolution `.ico`. Mica ships no third-party artwork.
- **A separate app, not a browser tab.** Its own taskbar button, its own pinning target, its
  own icon, its own entry in Settings › Installed apps.
- **Its own signed-in session.** Each app gets a private WebView2 profile, so two apps on the
  same domain stay logged into different accounts and sessions survive restarts.
- **Nothing in the way.** The toolbar is hidden until <kbd>Alt</kbd>, and pinnable.
  Settings are deliberately two things: zoom, and extensions.
- **Extensions from the Chrome Web Store.** The store's *Add to Chrome* button cannot install
  anything inside an embedded browser — it degrades to a download that fails. Mica intercepts
  that download and does the install itself, and offers a one-click banner whenever you land on
  an extension's store page. Pasting the extension's link into settings works too. Want an ad
  blocker? Install the one you like — Mica does not pick for you.
- **Web notifications become Windows toasts**, attributed to the app rather than to a browser.
- **Links stay sensible.** Off-site links you click open in your real browser; redirects and
  sign-in hops stay inside the app so a login never strands your session elsewhere.

## How it works

One host executable serves every app. What makes them behave like separate applications is the
per-app **AppUserModelID**: Windows groups taskbar buttons, resolves pinning and attributes
toasts by that id, not by executable path. Mica writes it in three places, and all three are
needed — on the shortcut, on the window before it is first shown (a window that reports no id
falls back to the executable, so every app would read as "Mica.Host"), and in
`HKCU\Software\Classes\AppUserModelId`, which is where the shell reads the name and icon.

That covers Windows' own taskbar. Plenty of shells — third-party docks especially — ignore the
AppUserModelID entirely and identify a running app by its **executable**, showing the icon
compiled into it. With a single shared host they all collapse into one nameless entry. So each
app also gets its own executable next to the shared host: a copy of `Mica.Host.exe` named after
the app, with its icon **and its version resource** rewritten. The icon alone is not enough —
those shells read `FileDescription` for the label, so a copy that keeps the host's version
resource is still called "Mica.Host" whatever the file is named.

The copy works because a .NET apphost stores the path of its managed assembly internally rather
than deriving it from its own file name. WinUI does not: it looks up its resource index as
`<executable name>.pri`, and a copy without one dies inside `Microsoft.UI.Xaml` before reaching
any app code. So a launcher is always a pair — `<slug>.exe` plus `<slug>.pri`, a few hundred
kilobytes. The 40 MB runtime stays shared.

Mica also puts *itself* in the Start menu on every run: an unpackaged app is invisible to Start
and to search until a shortcut exists. The same pass re-points every app's shortcut at the
current install, so moving or rebuilding Mica does not strand them.

> Because launchers are written next to the host, Mica installs per user (the pattern Chrome and
> VS Code use) rather than into Program Files. If the host folder is not writable, app creation
> falls back to the shared executable instead of failing.

```
%LOCALAPPDATA%\Mica\apps\<slug>\
├── app.json      name, start URL, in-app domains, zoom, pinned toolbar, window size
├── icon.ico      the site's icon at 16 → 256 px
├── icon.png      512 px, used by the builder's list
├── profile\      this app's WebView2 profile: cookies, storage, sign-in
├── extensions\   unpacked extensions installed from the Chrome Web Store
└── startup.log
```

```
<install folder>\<slug>.exe  +  <slug>.pri            the app's own executable
%APPDATA%\...\Start Menu\Programs\Mica\<Name>.lnk    → <slug>.exe --app <slug>
HKCU\...\Uninstall\Mica.<slug>                       → Settings › Installed apps
HKCU\...\Classes\AppUserModelId\Mica.App.<slug>      → the name and icon the shell shows
```

Removing an app deletes all of it: shortcut, profile, extensions, registry entry.

## Repository layout

```
Mica.slnx
├── Mica.Core/            Everything both apps share
│   ├── Shell/            BrowserShellPage — the window content of a generated app
│   ├── Apps/             AppRegistry (create/list/remove), ShortcutFactory (.lnk + AUMID),
│   │                     AppLauncherFactory + VersionResource (per-app executable,
│   │                     icon and version resources)
│   ├── Sites/            SiteInspector (name + icon), IconPipeline (render → .ico), HostRules
│   ├── Extensions/       CrxInstaller — the Chrome Web Store install path
│   ├── Integration/      Notifications, single instance, startup log
│   ├── WebAppConfig.cs   One app's configuration
│   └── MicaPaths.cs      Where everything lives
├── Mica.Host/            The shared executable every generated app runs on
└── Mica.Builder/         The Mica window: your apps, and the Add app flow
```

## Build & run

Prerequisites: .NET SDK 10, and the Windows App Runtime 2.3 (see [Deployment](#deployment)).

```powershell
dotnet build Mica.slnx -p:Platform=x64
.\Mica.Builder\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Mica.Builder.exe
```

Generated apps launch from their Start menu shortcut, or directly:

```powershell
.\Mica.Host\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Mica.Host.exe --app gemini
```

`--url <address>` runs the shell against a site without creating an app — useful for a quick
look before committing to one.

> Shortcuts created by a development build point into `bin\`. Rebuilding elsewhere, or deleting
> the folder, leaves them dangling; recreate the app, or remove it from Settings › Installed apps.

## Deployment

Mica is **unpackaged** on purpose. Requiring Developer Mode and an MSIX registration per app
would defeat the point of creating apps on demand — so apps are a folder plus a shortcut, and
creating one is instant.

It is also **framework-dependent** rather than self-contained, which is the one real trade-off
in the design: a self-contained Windows App SDK layout omits
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`, and without it `AppNotificationManager`
cannot register — so self-contained would cost web notifications entirely. The installer chains
the Windows App Runtime instead. Until that installer exists, install the runtime yourself:

```powershell
winget install Microsoft.WindowsAppRuntime.1.8
```

## Known limitations

- **Notifications only while the app is open.** There is no background push without a service.
- **Zoom is applied by the shell, not the browser.** WinUI's WebView2 control does not expose
  `ZoomFactor`, so Mica scales the document with Chromium's CSS `zoom`, which reflows layout the
  same way browser zoom does. <kbd>Ctrl</kbd> +/−/0 and <kbd>Ctrl</kbd>+wheel are routed to it.
- **SVG and WebP icons are rendered through headless Edge.** GDI+ cannot decode either, and most
  large sites now publish both. If Edge is unavailable, Mica falls back to the largest raster the
  site offers, then to a lettermark — creating an app never fails over an icon.
- **Extensions follow WebView2's rules.** Whatever manifest versions your WebView2 runtime
  accepts, Mica accepts.
- No installer yet, and no English UI yet — the app's interface is French.

## Legal

Code is released under the [MIT licence](LICENSE).

Mica ships no third-party trademarks or artwork. Each app fetches the name and icon published by
the site the user chose to wrap, from that site, at creation time. Extensions are downloaded from
Google's own update service at the user's explicit request and remain under their own licences.
Gemini, Google, Messenger, Facebook, Chrome and Windows are trademarks of their respective
owners; this project is not affiliated with, endorsed by, or sponsored by any of them.
