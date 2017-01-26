/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
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
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>.
    
 * Version Release Dates
 * Version: 0.1.0          24 Apr 2010
 * Version: 0.1.1          21 Nov 2010
 * Version: 0.1.2          28 Nov 2010
 * Version: 0.1.3          26 Dec 2010
 * Version: 0.1.4          20 Feb 2011
 * Version: 0.1.5          12 Jun 2011
 * Version: 0.1.6          30 Oct 2011
 * Version: 0.1.7          15 Apr 2012
 * Version: 0.1.8          16 Aug 2012
 * Version: 0.1.9          10 Feb 2013
 * Version: 0.2.0          26 Jan 2014
 * Version: 0.2.1+0.2.2    23 Mar 2014
 * Version: 0.2.3          07 May 2014
 * Version: 0.2.4          26 May 2014
 * Version: 0.2.4.1        31 Oct 2014
 * Version: 0.2.5          13 Jan 2015
 * Version: 0.2.6          04 Mar 2015
 * Version: 0.2.7          15 Mar 2015
 * Version: 0.2.8          20 Apr 2015
 * Version: 0.2.9          29 Oct 2015
 * ****************************************************************************************************************************/

using jaNETFramework;
using System;
using System.Diagnostics;

namespace jaNETProgram
{
    class Program
    {
        public static void Main(string[] args) {
            Application.Initialize();

            if (args.Length > 0) {
                foreach (string arg in args)
                    arg.Parse();
            }

            Console.Write("%copyright%".Parse() + "\r\n");

            while (Parser.ParserState) {
                try {
                    Console.Write(Environment.NewLine + Methods.Instance.WhoAmI() + "@jaNET>");
                    Console.ForegroundColor = ConsoleColor.Green;

                    string cmdReader = Console.ReadLine();

                    Console.ResetColor();

                    if (cmdReader.Length > 0) {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(cmdReader.Parse());
                        Console.ResetColor();
                    }
                }
                catch (ArgumentOutOfRangeException e) {
                    Debug.Print(e.Message);
                }
            }
        }
    }
}