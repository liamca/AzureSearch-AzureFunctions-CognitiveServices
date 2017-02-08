using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(OregonBriefsWebApp.Startup))]
namespace OregonBriefsWebApp
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
        }
    }
}
