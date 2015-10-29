/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2015
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of Project jaNET.

    Project jaNET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Project jaNET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>. */

namespace jaNETFramework
{
    static class User
    {
        //internal static volatile bool Status;

        static bool _status;

        internal static bool Status
        {
            get { return _status; }
            set
            {
                if (value != Status)
                    if (value)
                    {
                        _status = value;
                        doEventCheck(value);
                    }
                    else
                        _status = doEventCheck(value);
            }
        }

        static bool doEventCheck(bool status)
        {
            var method = Methods.Instance;
            if (status)
            {
                // Throw oncheckin event if exists
                if (method.GetEvent("oncheckin").Count > 0)
                    method.GetEvent("oncheckin").Item(0).InnerText.Parse();
            }
            else
            {
                // Throw oncheckout event if exists
                if (method.GetEvent("oncheckout").Count > 0)
                    method.GetEvent("oncheckout").Item(0).InnerText.Parse();
            }
            return status;
        }
    }
}
