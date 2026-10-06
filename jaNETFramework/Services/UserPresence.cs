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

using jaNET.Configuration;
using jaNET.Scripting;
using System;
using System.Linq;

namespace jaNET.Services;

/// <summary>
/// Whether the user is at home (%checkin% / %checkout%). Changing the state runs the "oncheckin" or "oncheckout" event.
/// The check-in event runs after the state became "present", the check-out event while the user still counts as present.
/// </summary>
internal sealed class UserPresence
{
    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;
    bool _present;

    public UserPresence(AppConfigStore config, Func<IInstructionExecutor> executor) {
        _config = config;
        _executor = executor;
    }

    public bool IsPresent {
        get => _present;
        set {
            if (value == _present) return;

            if (value) {
                _present = true;
                RunEvent("oncheckin");
            }
            else {
                RunEvent("oncheckout");
                _present = false;
            }
        }
    }

    public string Description => _present ? "present" : "absent";

    void RunEvent(string eventId) {
        try {
            string? action = _config.EventActions(eventId).FirstOrDefault();
            if (action != null) _executor().Run(action);
        }
        catch (Exception) {
            // a failing event handler must not prevent checking in or out
        }
    }
}
