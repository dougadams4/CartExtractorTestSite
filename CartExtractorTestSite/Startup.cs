using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(CartExtractorTestSite.Startup))]
namespace CartExtractorTestSite
{
    public partial class Startup {
        public void Configuration(IAppBuilder app) {
            ConfigureAuth(app);
        }
    }
}
