using System;
using System.Collections.Generic;
// using System.IO;    // IPluginConfigurationPage
// using System.Reflection;    // IPluginConfigurationPage

using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;

using Simkl.Configuration;

namespace Simkl
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        // public override string Name { get { return "Simkl TV Tracker"; } }
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/operators/lambda-operator#expression-body-definition
        public override string Name => "Simkl TV Tracker";
        public override string Description => "Scrobble your watched Movies, TV Shows and Anime to Simkl and share your progress with friends!";

        public override Guid Id => new Guid("2ecd91d5-b14b-4b92-8eb9-52c098edfc87");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer) {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        /* IHasWebPages */

        public IEnumerable<PluginPageInfo> GetPages() => new[]   // As we use new T inside, we can make it an implicitly typed array
            {
                new PluginPageInfo
                {
                    Name = "Simkl",
                    EmbeddedResourcePath = "Simkl.Configuration.configPage.html"
                }
            };

        public PluginConfiguration PluginConfiguration => Configuration;
    }
}
