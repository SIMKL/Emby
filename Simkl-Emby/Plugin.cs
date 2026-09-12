using System;
using System.Collections.Generic;
using System.IO;

using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Drawing;

using Simkl.Configuration;

namespace Simkl
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public const string StaticId = "Simkl";
        public const string StaticName = "Simkl";

        public override string Name => "Simkl TV Tracker";
        public override string Description => "Scrobble your watched Movies, TV Shows and Anime to Simkl and share your progress with friends!";

        public override Guid Id => new Guid("2ecd91d5-b14b-4b92-8eb9-52c098edfc87");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer) {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages() => new[]
            {
                new PluginPageInfo
                {
                    Name = "simkl_admin",
                    DisplayName = "Simkl",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.simkladmin.html",
                    IsMainConfigPage = true
                },
                new PluginPageInfo
                {
                    Name = "simkljs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.simkl.js"
                },
                new PluginPageInfo
                {
                    Name = StaticName,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.simkl.html",
                    EnableInUserMenu = true,
                    FeatureId = StaticId
                },
                new PluginPageInfo
                {
                    Name = "simkladminjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.simkladmin.js"
                }
            };

        public ImageFormat ThumbImageFormat {
            get {return ImageFormat.Jpg; }
        }

        public Stream GetThumbImage() {
            return GetType().Assembly.GetManifestResourceStream("Simkl.emby_thumb.jpg");
        }
    }
}
