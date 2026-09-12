namespace Simkl.Api.Responses
{
    public class ImportStatus
    {
        public bool running { get; set; }
        public string message { get; set; }             // current phase while running, final summary when done
        public string error { get; set; }               // null when fine
        public string lastImportAt { get; set; }        // ISO UTC or null
        public string lastAutoRunAt { get; set; }       // when the scheduled task last checked this user, ISO UTC or null
    }
}
