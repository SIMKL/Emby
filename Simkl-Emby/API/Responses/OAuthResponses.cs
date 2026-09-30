using System;

namespace Simkl.Api.Responses
{
    // POST /oauth2/device (RFC 8628 device authorization)
    public class DeviceCodeResponse
    {
        public string device_code { get; set; }              // server-side credential; never handed to the browser
        public string user_code { get; set; }                // shown to the user as XXXX-YYYY
        public string verification_uri { get; set; }
        public string verification_uri_complete { get; set; } // the same page with the code already filled in
        public int expires_in { get; set; }
        public int interval { get; set; }
    }

    // POST /oauth2/token (device_code and refresh_token grants share this shape)
    public class TokenResponse
    {
        public string access_token { get; set; }             // simkl_at_ prefixed
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }            // Simkl does not rotate it
        public string scope { get; set; }
    }

    // the RFC 6749 error envelope a 4xx from /oauth2 carries
    public class OAuthErrorResponse
    {
        public const string AuthorizationPending = "authorization_pending";
        public const string SlowDown = "slow_down";
        public const string ExpiredToken = "expired_token";
        public const string AccessDenied = "access_denied";
        public const string InvalidGrant = "invalid_grant";
        public const string InvalidClient = "invalid_client";
        public const string InvalidRequest = "invalid_request";
        public const string UnsupportedGrantType = "unsupported_grant_type";

        public string error { get; set; }
        public string error_description { get; set; }

        // true only for errors no amount of waiting or retrying will fix; everything else (a 502, a reset, a rate
        // limit) is transient and a poll or a renewal may go again
        public static bool IsTerminal(string error)
        {
            switch (error)
            {
                case ExpiredToken:
                case AccessDenied:
                case InvalidGrant:
                case InvalidClient:
                case InvalidRequest:
                case UnsupportedGrantType:
                    return true;
                default:
                    return false;
            }
        }
    }

    // what the settings page gets back from the device-login routes; the device code is deliberately never included
    public class DeviceLoginStatus
    {
        public const string Pending = "pending";
        public const string Connected = "connected";
        public const string Expired = "expired";
        public const string Error = "error";

        public string status { get; set; }
        public string userCode { get; set; }
        public string verificationUri { get; set; }
        public string verificationUriComplete { get; set; }
        public int expiresIn { get; set; }
        public int interval { get; set; }
        public string message { get; set; }
    }
}
