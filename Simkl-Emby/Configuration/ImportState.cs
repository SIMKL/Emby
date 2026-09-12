namespace Simkl.Configuration
{
    // Result and cursors of the history import, stored under its own typed-setting key that only HistoryImporter writes
    public class ImportState
    {
        public string lastImportAt { get; set; }        // "yyyy-MM-ddTHH:mm:ssZ" UTC or ""
        public string lastImportSummary { get; set; }   // Human text of the last run or ""
        public string fullImportAt { get; set; }        // when a full import last completed, or ""; decides full vs incremental
        public string syncedAt { get; set; }            // the activities "all" timestamp read by the last successful run (Simkl's clock), or ""; the date_from of the next incremental call
        public string lastAutoRunAt { get; set; }       // when the scheduled task (or the toggle being switched on) last checked this user, or ""; gates the per-user interval
        public string cursorShows { get; set; }         // newest /sync/activities watch timestamp covered by an import, per type, or ""
        public string cursorAnime { get; set; }
        public string cursorMovies { get; set; }
        public string cursorAccount { get; set; }       // hash of the Simkl token the cursors were taken under; a new login starts over

        public ImportState()
        {
            lastImportAt = "";
            lastImportSummary = "";
            fullImportAt = "";
            syncedAt = "";
            lastAutoRunAt = "";
            cursorShows = "";
            cursorAnime = "";
            cursorMovies = "";
            cursorAccount = "";
        }
    }
}
