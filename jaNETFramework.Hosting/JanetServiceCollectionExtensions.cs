/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2026
 * Author: John Ambeliotis
 *
 * License:
 *  This file is part of jaNET Framework.

    jaNET Framework is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    jaNET Framework is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */

using jaNET.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds jaNET to the services of a generic host (Worker Service, ASP.NET Core, ...).</summary>
public static class JanetServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="JanetHost"/> as a singleton and starts it, and stops it, with the application:
    /// the web server, the socket server, the serial port and the scheduler run as configured in AppConfig.xml.
    /// "%exit%" (from the console, the web or the socket) stops the application instead of ending the process.
    /// Unless you set <see cref="JanetHostOptions.RootDirectory"/> the files (AppConfig.xml, settings, www) live in the application folder.
    /// Inject <see cref="JanetHost"/> to run instructions: <c>host.Execute("judo schedule ls")</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional: change the <see cref="JanetHostOptions"/>, e.g. add commands, functions or another speaker.</param>
    public static IServiceCollection AddJanet(this IServiceCollection services, Action<JanetHostOptions>? configure = null) {
        services.AddSingleton(provider => {
            var options = new JanetHostOptions();
            configure?.Invoke(options);
            options.RootDirectory ??= Environment.GetEnvironmentVariable("JANET_HOME") is { Length: > 0 } home ? home : AppContext.BaseDirectory;   // not the working directory: a Windows service would get System32
            options.ExitProcess ??= provider.GetRequiredService<IHostApplicationLifetime>().StopApplication;
            return new JanetHost(options);
        });
        services.AddHostedService<JanetHostedService>();
        return services;
    }
}

/// <summary>Starts jaNET with the application and stops its services with it.</summary>
internal sealed class JanetHostedService : IHostedService
{
    readonly JanetHost _janet;
    readonly ILogger<JanetHostedService>? _logger;

    public JanetHostedService(JanetHost janet, ILogger<JanetHostedService>? logger = null) {
        _janet = janet;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        _janet.Start();

        // a web server that could not start, a damaged AppConfig.xml, a login that is still admin/admin: the application's log must say so
        foreach (HostNotice notice in _janet.Notices)
            _logger?.Log(notice.Level switch {
                NoticeLevel.Error => LogLevel.Error,
                NoticeLevel.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            }, "jaNET {Source}: {Message}", notice.Source, notice.Message);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _janet.Dispose();
        return Task.CompletedTask;
    }
}
