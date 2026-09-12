# Simkl's Emby Scrobbler
[![](https://img.shields.io/github/license/SIMKL/emby.svg?style=flat-square)][license]
[![](https://img.shields.io/gitlab/pipeline/daviddavo/simkl-emby.svg?style=flat-square)](https://gitlab.com/daviddavo/simkl-emby/pipelines)

## Install from Emby catalog automatically
1. Open Emby server Configuration page
2. In Advanced > Plugins click the Catalog tab
3. Scroll down to General section where you will find Simkl TV Tracker plugin. 
Short link: http://localhost:8096/web/index.html#!/plugins/plugincatalog.html
4. Click install
5. Restart Emby server
6. Open plugin settings and click log in, click the PIN link (you should be automatically logged in) and come back to Emby. If you see a blank settings page at this point, refresh the page and then you'll be able to see your Simkl profile name which means you are logged in and can change the settings.

## Installing Manually (Lastest build)
1. Download the lastest build of Simkl.dll [here](https://github.com/SIMKL/Emby/releases)
2. Put the plugin on your server's plugin folder
   - On Linux (Arch): `/usr/lib/emby-server/plugins/`
   - On Windows: `%AppData%\Emby-Server\programdata\plugins\`
   - or: `%AppData%\Roaming\Emby-Server\programdata\plugins\`
   - or: `%AppData%\Roaming\Emby-Server\plugins\`
3. Restart your server (using emby's web interface dashboard or Windows Task Manager)
4. Open Emby Browser Library
5. Select Expert -> Plugins -> Simkl TV Tracker -> Settings
6. Click Log in
7. Open the link (it already has your PIN) or visit https://simkl.com/pin/ and enter the provided PIN
8. Come back to Plugin settings, refresh browser if needed. 

You should now see your `Name` from Simkl there and `Scrobbling options` with checkmarks enabled:

`✔ Autoscrobbling Movies`

`✔ Autoscrobbling TV Shows`

`☐ Log rewatches on Simkl (Pro)` - when on, finishing something already in your Simkl history is logged as a rewatch (needs Simkl Pro or VIP)

Play, pause and stop are reported to Simkl as they happen and show on your Simkl profile as watching now; choose who can see that at https://simkl.com/settings/privacy/ (the plugin page links there). An item is marked as watched when you stop after 80%; stopping earlier saves your position on Simkl, where the Playback Progress Manager lists it (Emby keeps its own resume points).

Requires Emby Server 4.8.11 or newer (the plugin is built against that SDK; per-user settings do not exist before 4.8).

## Import your Simkl history into Emby
On your Simkl page in Emby press `Import changes since the last import`. Movies and episodes in your Emby library that you watched on Simkl since the last import are marked as played in Emby, with the date you watched them; the very first import goes through your whole history. Nothing is sent from Emby to Simkl. The status line under the button shows the result of the last run. `Reset and re-import fully`, under "Start over" at the end of the section, goes through the whole history again and also refreshes the watch dates of items Emby already has as played.

Switch on `Automatically import new Simkl history` to let Emby check Simkl regularly and mark only what you watched since the last import; pick how often under `Check for new history` (every hour up to every week, default once a day). The very first import, automatic or manual, goes through your whole history; every later run only picks up what is new.

## How to enable notifications when something is marked as watched
1. On Emby's dashboard, you'll have to go to the bottom and, on expert options, select "Notifications"
2. There, on section "Simkl Scrobbling", enable both notifications

## How to enable debugging
To report a bug or an error, we'll need more info to know how to fix it. To send us the needed reports you'll need to
first enable debug logging.

1. On Emby's dashboard, scroll to the bottom and, on expert options, select "Logs" (right above "Notifications")
2. Click on "Enable debug logging"
3. Restart the server and reproduce the error

Now you can contact us to try and fix the problem

## Current features
- Multi-user support
- Auto scrobble Movies and TV Shows at given percentage to Simkl
- Marking a movie, an episode, a season or a whole series as played in Emby marks it as watched on Simkl too, following the same checkboxes. Unmarking a single movie or episode also removes it from your Simkl history. Unmarking a whole season or series does not touch your Simkl history, by design (recognised by several episodes changing at once; a show with a single episode in Emby cannot be told apart from a single click), so a bulk change in Emby cannot wipe out your watch history
- Easy login using pin (no more putting passwords with the TV remote)
- If scrobbling fails, search it using filename using Simkl's API and then scrobble it
- Send notifications about scrobbling

## TODO
- [x] Auto-build for manual installing
- [ ] Auto-release

## Join the conversation:
- Discuss on Discord https://discord.gg/JRtwsfG
- Post bugs, feature requests on Github https://github.com/SIMKL/Emby/issues

[license]: https://github.com/SIMKL/Emby/blob/master/LICENSE
