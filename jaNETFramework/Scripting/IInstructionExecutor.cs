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

namespace jaNET.Scripting;

/// <summary>The shape of the answer to an instruction: the web UI asks for JSON, a browser for HTML, everything else for text.</summary>
internal enum ResponseFormat
{
    Html,
    Json,
    Text
}

/// <summary>Runs instruction text ("whoami", "yes; no", "judo schedule ls") the way every front end does.</summary>
internal interface IInstructionExecutor
{
    /// <param name="input">One or more instructions separated by ; or &amp;</param>
    /// <param name="format">How the results of the instructions are combined</param>
    /// <param name="silent">Do not speak the result and do not mail it (the "{mute}" and "{widget}" markers do the same)</param>
    string Run(string input, ResponseFormat format = ResponseFormat.Text, bool silent = false);
}

/// <summary>Raised when an instruction (or the "*pointer" to one) does not exist.</summary>
internal sealed class InstructionNotFoundException : System.Exception
{
    public InstructionNotFoundException(string name) : base(name + ", not found.") { }
}
