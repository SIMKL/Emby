define([
    "loading",
    "baseView",
    "toast",
    "alert",
    "confirm",
    "globalize",
    "emby-button",
    "emby-checkbox",
    "emby-toggle",
    "emby-linkbutton",
    "emby-select"

], function (loading, BaseView, toast, alert, confirm, globalize) {

    "use strict";

    var CONFIG_KEY = "simkl";
    var IMPORT_POLL_MS = 2000;
    var IMPORT_POLL_RETRIES = 5;

    function fetchExistingConfiguration(userId) {
        return ApiClient.getTypedUserSettings(userId, CONFIG_KEY);
    }

    // Re-fetch then patch, so fields the page does not show (userToken, min_length, ...) survive.
    // Saves are chained per view: each one reads the stored settings and writes them back whole, so a save started while
    // another was in flight would otherwise undo it (toggle off, then Save right away).
    function saveConfiguration(instance, patch) {
        var userId = instance.params.userId;
        var run = function () {
            loading.show();
            return fetchExistingConfiguration(userId).then(function (currentUserConfig) {
                currentUserConfig = currentUserConfig || {};
                patch(currentUserConfig);
                return ApiClient.updateTypedUserSettings(userId, CONFIG_KEY, currentUserConfig);
            }).then(function () {
                loading.hide();
                toast(globalize.translate("SettingsSaved"));
                if (instance.view && instance._saveChain === saved) loadConfiguration(instance);   // only the last save of a burst re-renders, so edits made meanwhile stay on screen
                return true;
            }, function (err) {
                loading.hide();
                alert(describeError(err, "Saving settings"));
                return false;
            });
        };
        var saved = (instance._saveChain || Promise.resolve()).then(run);
        instance._saveChain = saved;
        return saved;
    }

    // read at the click, so a save queued behind another one keeps what was on screen when it was requested
    function scrobbleOptions(view) {
        return {
            scrobbleMovies: view.querySelector(".scrobbleMovies").checked,
            scrobbleShows: view.querySelector(".scrobbleShows").checked,
            syncWatchedMarks: view.querySelector(".syncWatchedMarks").checked,
            allowRewatch: view.querySelector(".allowRewatch").checked
        };
    }

    function applyScrobbleOptions(config, options) {
        config.scrobbleMovies = options.scrobbleMovies;
        config.scrobbleShows = options.scrobbleShows;
        config.syncWatchedMarks = options.syncWatchedMarks;
        config.allowRewatch = options.allowRewatch;
    }

    // Absolute /emby/... URL so it survives reverse-proxy base paths. Every plugin route is [Authenticated], so the
    // X-Emby-Token ApiClient adds on its own is enough.
    function simklGet(path) {
        return ApiClient.getJSON(ApiClient.getUrl(path));
    }

    function simklPost(path) {
        return ApiClient.ajax({ type: "POST", url: ApiClient.getUrl(path), dataType: "json" });
    }

    // ApiClient.fetch rejects with the Response for HTTP >= 400 and with a TypeError when the
    // request never reached a server (proxy refused it, CORS, DNS), so the two cases are separable.
    function describeError(err, what) {
        var status = err && err.status;
        var login = !what;
        what = what || "Simkl login";
        console.error(what + " request failed", err);

        // only the login routes call out to api.simkl.com; a 500 on a settings call is Emby's own
        if (login && (status === 500 || status === 503)) {
            return "Emby could not reach api.simkl.com. Check the server's outbound firewall or seedbox rules, then try again.";
        }
        if (!status || status === 404 || status === 502 || status === 504) {
            return "The Emby server could not be reached at " + ApiClient.serverAddress() +
                ". If you use a custom domain or reverse proxy, open Emby by its IP address and log in from there.";
        }
        return what + " failed (HTTP " + status + "). See browser console for details.";
    }

    var SECTIONS = [".loginButtonContainer", ".loggingIn", ".configOptionsContainer"];

    function showSection(view, selector) {
        SECTIONS.forEach(function (s) {
            view.querySelector(s).classList.toggle("hide", s !== selector);
        });
    }

    // keepServerSide is set when the pending code is already gone (a completed or expired login) or about to be
    // replaced by a fresh one, so only an explicit Cancel tells the server to drop the code it is still holding.
    function stopLoginProcess(instance, keepServerSide) {
        if (instance._loginTimer) { clearTimeout(instance._loginTimer); instance._loginTimer = null; }
        if (instance._remainingTimer) { clearInterval(instance._remainingTimer); instance._remainingTimer = null; }
        var attempt = instance._loginAttempt;
        instance._loginAttempt = null;
        instance._finish = null;
        if (attempt && !keepServerSide) {
            simklPost("Simkl/oauth/device/" + instance.params.userId + "/cancel").catch(function () { /* the code expires on its own */ });
        }
        if (instance.view) showSection(instance.view, ".loginButtonContainer");
    }

    function abortLogin(instance, message) {
        loading.hide();
        alert(message);
        stopLoginProcess(instance);
    }

    function scheduleCheck(instance, attempt, interval) {
        instance._loginTimer = setTimeout(function () {
            checkLoginProcess(instance, attempt);
        }, (interval || 5) * 1000);
    }

    // _loginAttempt ties every poll to the attempt that started it: a response landing after Cancel, destroy or a
    // restarted login (instance.view may already be null) is dropped instead of acting on the new state. The token is
    // never returned here; on "connected" the page re-reads the stored config, which the server wrote.
    function checkLoginProcess(instance, attempt) {
        simklPost("Simkl/oauth/device/" + instance.params.userId + "/poll").then(function (status) {
            if (instance._loginAttempt !== attempt) return;

            if (status.status === "connected") {
                stopLoginProcess(instance, true);
                loadConfiguration(instance);
            } else if (status.status === "pending") {
                if (Date.now() > instance._finish) { abortLogin(instance, "The sign-in code expired. Please try again."); return; }
                attempt.failures = 0;
                scheduleCheck(instance, attempt, status.interval);
            } else if (status.status === "expired") {
                abortLogin(instance, "The sign-in code expired. Please try again.");
            } else {
                abortLogin(instance, status.message || "Error logging in.");
            }

        }, function (err) {
            if (instance._loginAttempt !== attempt) return;
            // the login keeps going on the server, so a dropped poll is retried a few times before giving up
            attempt.failures = (attempt.failures || 0) + 1;
            if (attempt.failures < 3) { scheduleCheck(instance, attempt, instance._loginInterval); return; }
            abortLogin(instance, describeError(err));
        });
    }

    function startLoginProcess(instance) {
        if (instance._loginStarting) return;   // a double-click would mint a second device code the page never shows
        var view = instance.view;
        var userId = instance.params.userId;
        stopLoginProcess(instance, true);   // a second click starts a fresh code; drop the previous attempt's timers, the new one supersedes it
        loading.show();

        var attempt = {};
        instance._loginAttempt = attempt;
        instance._loginStarting = true;

        simklPost("Simkl/oauth/device/" + userId).then(function (status) {
            instance._loginStarting = false;
            loading.hide();
            if (!instance.view || instance._loginAttempt !== attempt) return;   // destroyed or restarted while the code was being issued
            if (status.status !== "pending") { abortLogin(instance, status.message || "Could not start the sign-in. Try again."); return; }

            instance._finish = Date.now() + (status.expiresIn * 1000);
            instance._loginInterval = status.interval;

            view.querySelector(".loginText").innerHTML = "Please visit <a href='" + status.verificationUri +
                "' target='_blank'>" + status.verificationUri + "</a> on your phone or computer and enter the following code:";
            view.querySelector(".loginPin").textContent = status.userCode;

            var shortcut = view.querySelector(".loginShortcut");
            if (shortcut) {
                if (status.verificationUriComplete) {
                    shortcut.innerHTML = "Or <a href='" + status.verificationUriComplete + "' target='_blank'>open the page with the code already filled in</a>.";
                    shortcut.classList.remove("hide");
                } else {
                    shortcut.classList.add("hide");
                }
            }

            var secondsEl = view.querySelector(".loginSecondsRemaining");
            var renderRemaining = function () {
                secondsEl.textContent = Math.max(0, Math.round((instance._finish - Date.now()) / 1000));
            };
            renderRemaining();
            instance._remainingTimer = setInterval(renderRemaining, 1000);

            scheduleCheck(instance, attempt, status.interval);
            showSection(view, ".loggingIn");

        }, function (err) {
            instance._loginStarting = false;
            loading.hide();
            if (!instance.view || instance._loginAttempt !== attempt) return;   // destroyed or restarted while the code was being issued
            abortLogin(instance, describeError(err));
        });
    }

    // Logout is a server route, not a config save: the token is server-owned, and a saved config snapshot that left it
    // out would be written back over the login. The page re-reads the stored config afterwards.
    function logOut(instance) {
        loading.show();
        simklPost("Simkl/logout/" + instance.params.userId).then(function () {
            loading.hide();
            loadConfiguration(instance);
        }, function (err) {
            loading.hide();
            alert(describeError(err, "Logging out"));
        });
    }

    // The Simkl account behind the token is read on every load and never stored, so a re-login can never show a stale name.
    // the plan (free, pro, vip) comes with the account; only a known free account gets the support line, never an error.
    // Rewatches are a Pro feature: a known free account cannot tick the box, so its stops never carry allow_rewatch, and a
    // box ticked on a plan that has since lapsed is cleared here (saved by the next Save) and by the server on Simkl's answer
    function renderAccount(view, account) {
        var known = !!(account && account.id);
        var free = known && account.plan === "free";
        var plan = known && account.plan === "vip" ? " (VIP)" : known && account.plan === "pro" ? " (PRO)" : "";
        view.querySelector(".simklAccount").textContent = known && account.name
            ? "You are logged in to Simkl as " + account.name + plan + "."
            : "You are logged in to Simkl.";
        view.querySelector(".simklSupport").classList.toggle("hide", !free);
        view.querySelector(".simklRewatchNote").classList.toggle("hide", !free);
        // a login still on the old pin flow is offered a one-click reconnect onto the OAuth 2 device flow
        view.querySelector(".simklLegacyNotice").classList.toggle("hide", !(known && account.legacy));
        var rewatch = view.querySelector(".allowRewatch");
        rewatch.disabled = free;
        if (free) rewatch.checked = false;
        view.querySelector(".simklLinks").classList.toggle("hide", !known);
        if (known) {
            view.querySelector(".simklPlaybackLink").href = "https://simkl.com/" + account.id + "/history/playback-progress-manager/";
        }
    }

    // An empty account is also what the server answers after Simkl rejected the token and it was cleared, so the stored
    // config is re-read and the page re-rendered from it; the token check keeps a 200 without an account id from looping.
    function fetchAccount(instance) {
        var userId = instance.params.userId;   // params are nulled on destroy, the response may land after it
        simklGet("Simkl/account/" + userId).then(function (account) {
            if (account && account.id) {
                if (instance._importPaused) instance._loaded = false;   // landed while paused or destroyed: next onResume reloads
                else renderAccount(instance.view, account);
                return null;
            }
            return fetchExistingConfiguration(userId).then(function (config) {
                if (config && config.userToken) return;
                if (instance._importPaused) instance._loaded = false;   // next onResume reloads
                else loadConfiguration(instance);
            });
        }).catch(function (err) {
            console.error("Simkl account request failed", err);
        });
    }

    function importPath(instance) {
        return "Simkl/import/" + instance.params.userId;
    }

    function formatImportDate(iso) {
        var date = new Date(iso);
        return isNaN(date.getTime()) ? iso : date.toLocaleString();
    }

    function describeImportStatus(status) {
        if (status.running) return status.message || "Importing...";

        var text = status.message || "";
        if (status.lastImportAt) text = "Last import " + formatImportDate(status.lastImportAt) + ". " + text;
        return text || "No import has run yet.";
    }

    function stopImportPolling(instance) {
        if (instance._importTimer) { clearTimeout(instance._importTimer); instance._importTimer = null; }
    }

    function scheduleImportPoll(instance) {
        stopImportPolling(instance);
        instance._importTimer = setTimeout(function () {
            instance._importTimer = null;
            refreshImportStatus(instance);
        }, IMPORT_POLL_MS);
    }

    function setImportRunning(instance, running) {
        instance._importRunning = running;
        instance.view.querySelector(".btnSimklImport").disabled = running;
        instance.view.querySelector(".btnSimklReset").disabled = running;
    }

    // _importPaused guards the in-flight response that lands after onPause/destroy (destroy nulls instance.view).
    function applyImportStatus(instance, status) {
        if (instance._importPaused) { instance._loaded = false; return; }   // a status that could not be applied is re-read on resume
        instance._pollFailures = 0;

        var wasRunning = instance._importRunning;
        setImportRunning(instance, !!status.running);
        instance.view.querySelector(".importStatus").textContent = describeImportStatus(status);
        instance.view.querySelector(".autoImportStatus").textContent = status.lastAutoRunAt
            ? "Last automatic check " + formatImportDate(status.lastAutoRunAt) + "."
            : "No automatic check has run yet.";

        if (instance._importRunning) {
            scheduleImportPoll(instance);
        } else if (wasRunning) {
            if (status.error) alert(status.error); else toast("Import finished");
            loadConfiguration(instance);    // a run can end with the Simkl token cleared, so re-render from the stored config
        }
    }

    function refreshImportStatus(instance) {
        simklGet(importPath(instance) + "/status").then(function (status) {
            applyImportStatus(instance, status);
        }, function (err) {
            if (instance._importPaused) return;
            var view = instance.view;

            // the run keeps going on the server, so a failed poll is retried before giving up
            if (instance._importRunning) {
                instance._pollFailures = (instance._pollFailures || 0) + 1;
                if (instance._pollFailures < IMPORT_POLL_RETRIES) {
                    console.error("Simkl import status poll failed, retrying", err);
                    view.querySelector(".importStatus").textContent = "Waiting for the Emby server (retry " + instance._pollFailures + " of " + IMPORT_POLL_RETRIES + ")...";
                    scheduleImportPoll(instance);
                    return;
                }
                setImportRunning(instance, false);
                instance._loaded = false;   // next onResume reloads, so the result is picked up
                view.querySelector(".importStatus").textContent = "Lost the connection to Emby while the import was running. It continues on the server; reopen this page to see the result.";
                console.error("Simkl import status request failed", err);
                alert("Could not read the import status" + (err && err.status ? " (HTTP " + err.status + ")" : "") + ". The import keeps running on the server; reopen this page to see the result.");
                return;
            }

            setImportRunning(instance, false);
            alert(describeError(err, "Simkl import status"));
        });
    }

    // latest changes = what Simkl reports as watched since the last import (the very first run takes everything);
    // everything = forget what was imported before and go through the whole history, refreshing Simkl's watch dates
    // on items Emby already has as played
    function startImport(instance, everything) {
        confirm({
            title: everything ? "Reset and re-import fully" : "Import changes since the last import",
            text: everything
                ? "Goes through your whole Simkl history again. Movies and episodes already marked as played in Emby get the watch date from Simkl. Nothing is unmarked and nothing is sent to Simkl."
                : "Asks Simkl what you watched since the last import and marks those movies and episodes as played in Emby. The very first import goes through your whole history. Nothing is sent to Simkl.",
            confirmText: "Import"
        }).then(function () {
            loading.show();
            simklPost(importPath(instance) + (everything ? "/reset" : "/check")).then(function (status) {
                loading.hide();
                if (status.running) {
                    applyImportStatus(instance, status);
                    if (everything) toast("Re-import started");   // the status line sits under the main button, not next to this link
                } else {
                    alert(status.error || status.message || "Import could not be started.");
                }
            }, function (err) {
                loading.hide();
                alert(describeError(err, "Simkl import"));
            });
        }, function () {
            // cancelled
        });
    }

    // Switching automatic import on runs the same check the scheduled task does, right away:
    // a full import when nothing was imported yet, otherwise the cheap incremental one.
    // userId comes from the caller: the page may have been left while the save before this check was running
    function startAutoCheck(instance, userId) {
        // read before the request: the reload saveConfiguration just issued may poll the new run before this response lands
        var tracked = instance._importRunning;
        simklPost("Simkl/import/" + userId + "/check?automatic=true").then(function (status) {
            if (instance._importPaused) { instance._loaded = false; return; }
            if (status.error && !status.running) { alert(status.error); return; }
            if (status.running && !tracked) toast("Checking Simkl for new history");
            applyImportStatus(instance, status);
        }, function (err) {
            console.error("Simkl automatic check request failed", err);
        });
    }

    function loadConfiguration(instance) {
        var view = instance.view;
        var userId = instance.params.userId;

        loading.show();

        fetchExistingConfiguration(userId).then(function (currentUserConfig) {
            if (!instance.view) { loading.hide(); return; }   // navigated away while loading: the view is gone and params are null

            currentUserConfig = currentUserConfig || {};

            if (currentUserConfig.userToken) {
                renderAccount(view, null);
                fetchAccount(instance);
                view.querySelector(".scrobbleMovies").checked = currentUserConfig.scrobbleMovies !== false;
                view.querySelector(".scrobbleShows").checked = currentUserConfig.scrobbleShows !== false;
                // !== false, like the two above: a user stored before this setting existed has no value for it and
                // must keep mirroring, which is what the server side defaults to as well
                view.querySelector(".syncWatchedMarks").checked = currentUserConfig.syncWatchedMarks !== false;
                view.querySelector(".allowRewatch").checked = currentUserConfig.allowRewatch === true;
                view.querySelector(".autoImport").checked = currentUserConfig.autoImport === true;
                view.querySelector(".autoImportHours").value = String(currentUserConfig.autoImportHours || 24);
                view.querySelector(".autoImportInterval").classList.toggle("hide", currentUserConfig.autoImport !== true);
                showSection(view, ".configOptionsContainer");
                refreshImportStatus(instance);
            } else {
                stopImportPolling(instance);
                instance._importRunning = false;
                showSection(view, ".loginButtonContainer");
            }

            instance._loaded = true;
            loading.hide();
        }, function (err) {
            loading.hide();
            if (instance.view) alert(describeError(err, "Loading settings"));
        });
    }

    function onSubmit(ev) {
        ev.preventDefault();

        var options = scrobbleOptions(this.view);
        saveConfiguration(this, function (config) { applyScrobbleOptions(config, options); });

        return false;
    }

    function bindEvents(instance) {
        var view = instance.view;
        view.querySelector("form").addEventListener("submit", onSubmit.bind(instance));
        view.querySelector(".btnSimklLogin").addEventListener("click", function () { startLoginProcess(instance); });
        view.querySelector(".btnSimklReconnect").addEventListener("click", function () { startLoginProcess(instance); });
        view.querySelector(".btnSimklCancel").addEventListener("click", function () { stopLoginProcess(instance); });
        view.querySelector(".btnSimklLogout").addEventListener("click", function () { logOut(instance); });
        view.querySelector(".btnSimklImport").addEventListener("click", function () { startImport(instance, false); });
        view.querySelector(".btnSimklReset").addEventListener("click", function () { startImport(instance, true); });
        view.querySelector(".autoImport").addEventListener("change", function () {
            var toggle = this, checked = toggle.checked, options = scrobbleOptions(view), userId = instance.params.userId;
            // the reload after saving re-renders the checkboxes, so unsaved scrobble edits are carried along
            saveConfiguration(instance, function (config) {
                applyScrobbleOptions(config, options);
                config.autoImport = checked;
            }).then(function (saved) {
                if (!saved) toggle.checked = !checked;
                else if (checked) startAutoCheck(instance, userId);
            });
        });
        view.querySelector(".autoImportHours").addEventListener("change", function () {
            var hours = parseInt(this.value, 10) || 24, options = scrobbleOptions(view);
            saveConfiguration(instance, function (config) {
                applyScrobbleOptions(config, options);
                config.autoImportHours = hours;
            });
        });
    }

    function View(view, params) {
        BaseView.apply(this, arguments);
        bindEvents(this);
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        this._importPaused = false;
        if (options.refresh || !this._loaded) {
            loadConfiguration(this);
        } else if (this._importRunning) {
            refreshImportStatus(this);
        }
    };

    View.prototype.onPause = function () {
        this._importPaused = true;
        stopImportPolling(this);
        BaseView.prototype.onPause.apply(this, arguments);
    };

    View.prototype.destroy = function () {
        this._importPaused = true;
        stopImportPolling(this);
        stopLoginProcess(this);
        BaseView.prototype.destroy.apply(this, arguments);
    };

    return View;
});
