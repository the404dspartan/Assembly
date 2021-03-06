﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Blamite.Blam;
using Blamite.Blam.Scripting;
using Blamite.Blam.Util;
using Blamite.Flexibility;

namespace Blamite.Flexibility
{
    public class BuildInformation
    {
        private string _gameName;
        private AESKey _localeKey;
        private AESKey _stringidKey;
        private AESKey _filenameKey;
        private string _shortName;
        private string _pluginFolder;
        private int _headerSize;
        private bool _loadStrings;
        private string _scriptDefsFile;
        private StructureLayoutCollection _layouts = new StructureLayoutCollection();
        private LocaleSymbolCollection _localeSymbols = new LocaleSymbolCollection();
        private IStringIDResolver _stringIDResolver;
        private int _segmentAlignment;
        private VertexLayoutCollection _vertexLayouts = new VertexLayoutCollection();

        public class StructureLocaleSymbol
        {
            public char Code { get; set; }
            public string CodeAsString { get; set; }
            public string Display { get; set; }
        }

        public BuildInformation(string game, string localeKey, string stringidKey, IStringIDResolver stringIDResolver, string filenameKey, int headerSize, bool loadStrings, StructureLayoutCollection layouts, string shortName, string pluginFolder, string scriptDefsFile, int segmentAlignment)
        {
            _gameName = game;
            if (localeKey != null)
                _localeKey = new AESKey(localeKey);
            if (stringidKey != null)
                _stringidKey = new AESKey(stringidKey);
            _stringIDResolver = stringIDResolver;
            if (filenameKey != null)
                _filenameKey = new AESKey(filenameKey);
            _headerSize = headerSize;
            _loadStrings = loadStrings;
            _layouts = layouts;
            _shortName = shortName;
            _pluginFolder = pluginFolder;
            _scriptDefsFile = scriptDefsFile;
            _segmentAlignment = segmentAlignment;
        }

        public void AddLayout(string name, StructureLayout layout)
        {
            _layouts.AddLayout(name, layout);
        }

        public StructureLayout GetLayout(string name)
        {
            return _layouts.GetLayout(name);
        }

        public bool HasLayout(string name)
        {
            return _layouts.HasLayout(name);
        }

        public LocaleSymbolCollection LocaleSymbols
        {
            get { return _localeSymbols; }
        }

        public VertexLayoutCollection VertexLayouts
        {
            get { return _vertexLayouts; }
        }

        public string GameName
        {
            get { return _gameName; }
        }

        public string ShortName
        {
            get { return _shortName; }
        }

        public string PluginFolder
        {
            get { return _pluginFolder; }
        }

        public AESKey LocaleKey
        {
            get { return _localeKey; }
        }

        public AESKey StringIDKey
        {
            get { return _stringidKey; }
        }

        public IStringIDResolver StringIDResolver
        {
            get { return _stringIDResolver; }
        }

        public AESKey FileNameKey
        {
            get { return _filenameKey; }
        }

        public int HeaderSize
        {
            get { return _headerSize; }
        }

        public bool LoadStrings
        {
            get { return _loadStrings; }
        }

        public string ScriptDefinitionsFilename
        {
            get { return _scriptDefsFile; }
        }

        public int SegmentAlignment
        {
            get { return _segmentAlignment; }
        }
    }
}