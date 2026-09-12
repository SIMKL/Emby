define([
    "baseView",
    "appRouter",
    "emby-button"

], function (BaseView, appRouter) {

    "use strict";

    var CONFIG_KEY = "simkl";
    var USER_PAGE = "configurationpage?name=Simkl&userId=";

    function escapeHtml(text) {
        return String(text || "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function getStatus(userId) {
        return ApiClient.getTypedUserSettings(userId, CONFIG_KEY).then(function (config) {
            return config && config.userToken ? "Connected to Simkl" : "Not connected";
        }, function () {
            return "Not connected";
        });
    }

    function renderUser(user, status) {
        return '<div class="listItem listItem-border">' +
            '<div class="listItemBody two-line">' +
            '<h3 class="listItemBodyText">' + escapeHtml(user.Name) + '</h3>' +
            '<div class="listItemBodyText secondary">' + escapeHtml(status) + '</div>' +
            '</div>' +
            '<button is="emby-button" type="button" class="raised raised-mini btnUserSimkl" data-id="' + escapeHtml(user.Id) + '"><span>Simkl settings</span></button>' +
            '</div>';
    }

    function loadUsers(view) {
        var container = view.querySelector(".simklUserList");

        ApiClient.getUsers().then(function (users) {
            return Promise.all(users.map(function (user) {
                return getStatus(user.Id).then(function (status) {
                    return renderUser(user, status);
                });
            }));
        }).then(function (rows) {
            container.innerHTML = rows.join("") || "<p>No users found.</p>";
        }, function () {
            container.innerHTML = "<p>Could not load users.</p>";
        });
    }

    function View(view, params) {

        BaseView.apply(this, arguments);

        view.addEventListener("click", function (e) {
            var btn = e.target.closest(".btnUserSimkl");
            if (!btn) return;
            appRouter.show(USER_PAGE + encodeURIComponent(btn.getAttribute("data-id")));
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {

        BaseView.prototype.onResume.apply(this, arguments);
        loadUsers(this.view);
    };

    return View;
});
