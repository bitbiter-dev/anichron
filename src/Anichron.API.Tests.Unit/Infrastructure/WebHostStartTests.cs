using Anichron.API.Services;
using Anichron.Core.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class WebHostStartTests
{
    [Fact]
    public async Task Start_OpensNoDatabaseConnectionAsync()
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = new WebApplicationFactory<AuthService>().WithWebHostBuilder(web => web
            .UseSetting("Jwt:Secret", "web-host-start-test-secret-at-least-32-bytes")
            .UseSetting("POSTGRES_CONNECTION:HOST", "unreachable.invalid")
            .UseSetting("POSTGRES_CONNECTION:DBNAME", "anichron")
            .UseSetting("POSTGRES_CONNECTION:USER", "anichron")
            .UseSetting("POSTGRES_CONNECTION:PASSWORD", "anichron")
            .ConfigureTestServices(services =>
                services.ConfigureDbContext<AnichronDbContext>(options => options.AddInterceptors(new ConnectionTouch(opened)))));

        var start = Task.Run(() => factory.Server, TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(start, opened.Task);

        first.Should().BeSameAs(start, "starting the web host must not open a database connection");
        await start;
    }

    private sealed class ConnectionTouch(TaskCompletionSource opened) : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            opened.TrySetResult();
            throw new InvalidOperationException("The web host opened a database connection.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            opened.TrySetResult();
            throw new InvalidOperationException("The web host opened a database connection.");
        }
    }
}
