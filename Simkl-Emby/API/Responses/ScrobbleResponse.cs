namespace Simkl.Api.Responses
{
    public class ScrobbleResponse
    {
        public long? id { get; set; }
        public string action { get; set; }
        public double? progress { get; set; }
        public long? rewatch_id { get; set; }           // only with allow_rewatch=yes on a stop that marked something watched
        public string rewatch_status { get; set; }      // active, completed, closed, first_watch, too_soon, not_eligible, pro_required
    }
}
