/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
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
    class InstructionSet
    {
        internal string Id { get; set; }
        internal string Action { get; set; }
        internal string Category { get; set; }
        internal string Header { get; set; }
        internal string ShortDescription { get; set; }
        internal string Description { get; set; }
        internal string ThumbnailUrl { get; set; }
        internal string Reference { get; set; }

        internal InstructionSet(string action) :
            this(string.Empty, action, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty) {

        }

        internal InstructionSet(string id, string action) :
            this(id, action, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty) {

        }

        internal InstructionSet(string id, string action, string category, string header, string shortDescription, string description, string thumbnailUrl, string reference) {
            Id = id;
            Action = action;

            if (!String.IsNullOrEmpty(category))
                Category = category;
            if (!String.IsNullOrEmpty(header))
                Header = header;
            if (!String.IsNullOrEmpty(shortDescription))
                ShortDescription = shortDescription;
            if (!String.IsNullOrEmpty(description))
                Description = description;
            if (!String.IsNullOrEmpty(thumbnailUrl))
                ThumbnailUrl = thumbnailUrl;
            if (!String.IsNullOrEmpty(reference))
                Reference = reference;
        }
    }
}