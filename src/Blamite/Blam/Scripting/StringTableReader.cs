﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Blamite.Blam.Util;
using Blamite.IO;

namespace Blamite.Blam.Scripting
{
    public class StringTableReader
    {
        private SortedSet<int> _requestedStrings = new SortedSet<int>();

        public void RequestString(int offset)
        {
            _requestedStrings.Add(offset);
        }

        public void ReadRequestedStrings(IReader reader, int tableOffset, CachedStringTable output)
        {
            int lastEnd = -1;
            foreach (int offset in _requestedStrings)
            {
                if (offset <= lastEnd)
                    continue;

                reader.SeekTo(tableOffset + offset);
                string str = reader.ReadAscii();
                output.CacheString(offset, str);
                lastEnd = offset + str.Length;
            }
        }
    }
}
