namespace Simkl.Api.Responses
{
    // GET /sync/activities; every value is "yyyy-MM-ddTHH:mm:ssZ" or null. Only the watch-related keys are declared.
    // The dropped list is keyed "notinteresting" for this app id and "dropped" for newer ones, so both are declared.
    public class ActivitiesResponse
    {
        public string all { get; set; }     // newest activity of any kind, on Simkl's clock: the watermark of the next incremental call
        public ActivitiesBucket tv_shows { get; set; }
        public ActivitiesBucket anime { get; set; }
        public ActivitiesBucket movies { get; set; }
    }

    public class ActivitiesBucket
    {
        public string watching { get; set; }
        public string completed { get; set; }
        public string hold { get; set; }
        public string dropped { get; set; }
        public string notinteresting { get; set; }
    }
}
