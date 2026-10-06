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

namespace jaNETProgram.Terminal;

/// <summary>What the console loop needs from a user interface (plain text or fancy).</summary>
interface IUi
{
    /// <summary>Shows the product name, version, copyright and a pending-update notice.</summary>
    void ShowBanner();

    /// <summary>Shows which services (web server, socket, serial port, ...) came up. Called after initialization.</summary>
    void ShowServices();

    /// <summary>Prompts and reads a line. Returns null when the input ended.</summary>
    string? ReadCommand();

    /// <summary>Executes a non-empty command and shows its result.</summary>
    void Run(string command);

    void ShowGoodbye();
}
