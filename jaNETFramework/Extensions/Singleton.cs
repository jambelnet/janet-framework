/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2016
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

using System;

namespace jaNETFramework
{
    // public static <T> Instance { get { return Singleton<T>.Instance; } }
    internal sealed class Singleton<T> where T : class, new()
    {
        private static readonly Lazy<T> lazy =
            new Lazy<T>(() => new T());

        public static T Instance { get { return lazy.Value; } }

        private Singleton()
        {

        }
    }
}