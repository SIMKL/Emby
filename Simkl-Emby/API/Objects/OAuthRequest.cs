namespace Simkl.Api.Objects
{
    // body of a POST to /oauth2/device and /oauth2/token; the member names are the wire names the serializer emits,
    // and Simkl ignores the ones a given grant does not use
    public class OAuthRequest
    {
        public const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";
        public const string RefreshTokenGrant = "refresh_token";

        public string client_id { get; set; }
        public string grant_type { get; set; }
        public string scope { get; set; }
        public string device_code { get; set; }
        public string refresh_token { get; set; }
    }
}
