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

using System;
using System.Threading.Tasks;

namespace jaNET.Infrastructure;

/// <summary>
/// Whether jaNET is (still) meant to run. "%exit%" asks to stop from anywhere (console, web, socket): from then on
/// <see cref="IsRunning"/> is false. A moment later (so that the answer to the request that asked can still be delivered)
/// the services are stopped and the process ends, even if the console is waiting for input.
/// </summary>
internal sealed class HostLifetime
{
    static readonly TimeSpan DefaultExitDelay = TimeSpan.FromSeconds(1);

    readonly Action _exitProcess;
    readonly TimeSpan _exitDelay;
    volatile bool _running = true;

    /// <param name="exitProcess">Ends the process (Environment.Exit); tests pass a counter.</param>
    /// <param name="exitDelay">How long to wait before stopping and exiting; default one second.</param>
    public HostLifetime(Action exitProcess, TimeSpan? exitDelay = null) {
        _exitProcess = exitProcess;
        _exitDelay = exitDelay ?? DefaultExitDelay;
    }

    public bool IsRunning => _running;

    /// <summary>Raised once, after the exit delay and before the process ends: stop the services here.</summary>
    public event Action? StopRequested;

    public void RequestStop() {
        if (!_running) return;
        _running = false;

        _ = Task.Run(async () => {
            await Task.Delay(_exitDelay).ConfigureAwait(false);
            StopRequested?.Invoke();
            _exitProcess();
        });
    }
}

/// <summary>The terminal, as far as "%clear%" is concerned.</summary>
internal interface IConsoleControl
{
    void Clear();
}

internal sealed class ConsoleControl : IConsoleControl
{
    public void Clear() {
        try { Console.Clear(); }
        catch (System.IO.IOException) { /* output is redirected: there is nothing to clear */ }
    }
}
