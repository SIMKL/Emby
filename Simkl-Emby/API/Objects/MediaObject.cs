using MediaBrowser.Model.Dto;

namespace Simkl.Api.Objects
{
    public abstract class MediaObject
    {
        public abstract SimklIds ids { get; set; }
    }
}
