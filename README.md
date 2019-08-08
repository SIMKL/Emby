# Simkl's Emby Scrobbler
[![](https://img.shields.io/github/license/SIMKL/emby.svg?style=flat-square)][license]
[![](https://img.shields.io/gitlab/pipeline/daviddavo/simkl-emby.svg?style=flat-square)](https://gitlab.com/daviddavo/simkl-emby/pipelines)

## Installing Manually (Lastest build)
1. Download the lastest build [here](https://gitlab.com/daviddavo/simkl-emby/-/jobs/artifacts/master/raw/Simkl.dll?job=build)
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

`Scrobbling percentage: 70`

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
