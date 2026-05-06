namespace Anichron.API.Infrastructure;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddAppConfiguration()
        {
            var iniPath = Path.Combine(builder.Environment.ContentRootPath, "configuration", "app.ini");
            AppIniInitializer.EnsureExists(iniPath);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
