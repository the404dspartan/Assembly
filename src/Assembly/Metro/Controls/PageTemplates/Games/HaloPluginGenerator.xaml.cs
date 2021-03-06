﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
using Assembly.Metro.Dialogs;
using Blamite.Blam;
using Blamite.Blam.ThirdGen;
using Blamite.Flexibility;
using Blamite.IO;
using Blamite.Plugins;
using Blamite.Plugins.Generation;
using Blamite.Util;

namespace Assembly.Metro.Controls.PageTemplates.Games
{
    /// <summary>
    /// Interaction logic for PluginGenerator.xaml
    /// </summary>
    public partial class HaloPluginGenerator
    {
		private BuildInfoLoader _buildLoader;
        public ObservableCollection<MapEntry> GeneratorMaps = new ObservableCollection<MapEntry>();
		private bool _isWorking;

        public HaloPluginGenerator()
        {
            InitializeComponent();
            DataContext = GeneratorMaps;

			var supportedBuilds = XDocument.Load(@"Formats\SupportedBuilds.xml");
            _buildLoader = new BuildInfoLoader(supportedBuilds, @"Formats\");
        }

        public class MapEntry
        {
            public string MapName { get; set; }
            public string LocalMapPath { get; set; }
            public bool IsSelected { get; set; }
        }

        public bool Close()
        {
			return !_isWorking;
        }

        private void btnInputFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog
	                      {
		                      SelectedPath =
			                      Directory.Exists(txtInputFolder.Text)
				                      ? txtInputFolder.Text
				                      : Helpers.VariousFunctions.GetApplicationLocation()
	                      };
	        if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

	        GeneratorMaps.Clear();
	        txtInputFolder.Text = fbd.SelectedPath;

	        var di = new DirectoryInfo(txtInputFolder.Text);
	        var fis = di.GetFiles("*.map");
	        foreach (var fi in fis.Where(fi => !fi.Name.ToLower().StartsWith("campaign") && !fi.Name.ToLower().StartsWith("shared") && !fi.Name.ToLower().StartsWith("single_player_shared")))
	        {
		        GeneratorMaps.Add(new MapEntry
			                          {
				                          IsSelected = true,
				                          LocalMapPath = fi.FullName,
				                          MapName = fi.Name
			                          });
	        }
        }
        private void btnOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog();
            if (Directory.Exists(txtOutputFolder.Text))
                fbd.SelectedPath = txtOutputFolder.Text;
            else
                fbd.SelectedPath = Helpers.VariousFunctions.GetApplicationLocation() + "\\plugins\\";
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtOutputFolder.Text = fbd.SelectedPath;
            }
        }


        private void btnGeneratePlugins_Click(object sender, RoutedEventArgs e)
        {
			// Check that all needed info is loaded
			if (GeneratorMaps == null || ((String.IsNullOrEmpty(txtInputFolder.Text) || !Directory.Exists(txtInputFolder.Text)) ||
											(String.IsNullOrEmpty(txtOutputFolder.Text) || !Directory.Exists(txtOutputFolder.Text)) ||
											(GeneratorMaps.Count(entry => !entry.IsSelected) == GeneratorMaps.Count)))
			{
				MetroMessageBox.Show("Missing required information", "Required information for plugin generation is missing...");
				return;
			}

			StartGeneration();
        }
		private void StartGeneration()
		{
			_isWorking = true;

			btnInputFolder.IsEnabled =
				btnOutputFolder.IsEnabled =
				btnGeneratePlugins.IsEnabled = false;

            MaskingPage.Visibility = Visibility.Visible;

            var generatorMaps = GeneratorMaps.Where((m) => m.IsSelected).ToList();
			var outputPath = txtOutputFolder.Text;

			var worker = new BackgroundWorker();
			worker.DoWork += (o, args) => worker_DoWork(o, args, generatorMaps, outputPath, worker);
			worker.WorkerReportsProgress = true;
			worker.ProgressChanged += worker_ProgressChanged;
			worker.RunWorkerCompleted += worker_RunWorkerCompleted;
			worker.RunWorkerAsync();
		}
		private void EndGeneration()
		{
			_isWorking = false;

			btnInputFolder.IsEnabled =
				btnOutputFolder.IsEnabled =
				btnGeneratePlugins.IsEnabled = true;

			MaskingPage.Visibility = Visibility.Collapsed;
		}

		void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			if (e.Error == null)
				MetroMessageBox.Show("Plugin Generation Complete!", "Plugin generation completed successfully in " +
				                     Math.Round(((TimeSpan) e.Result).TotalSeconds, 3) + " seconds.");
			else
				MetroMessageBox.Show("Error Generating Plugins!", e.Error.ToString());

			EndGeneration();
		}
		void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			lblProgressStatus.Text = string.Format("Generating Plugins... ({0}%)", e.ProgressPercentage);
		}
		void worker_DoWork(object sender, DoWorkEventArgs e, IList<MapEntry> generatorMaps, string outputPath, BackgroundWorker worker)
		{
			var globalMaps = new Dictionary<string, MetaMap>();
			var startTime = DateTime.Now;
			var gameIdentifier = "";

            for (var i = 0; i < generatorMaps.Count; i++)
			{
				var tagMaps = new Dictionary<ITag, MetaMap>();

				IReader reader;
				var cacheData = LoadMap(generatorMaps[i].LocalMapPath, out reader);
				var cacheFile = cacheData.Key;
				var analyzer = new MetaAnalyzer(cacheFile);
				if (gameIdentifier == "")
					gameIdentifier = cacheData.Value.ShortName;

				var mapsToProcess = new Queue<MetaMap>();
				foreach (var tag in cacheFile.Tags)
				{
                    if (tag.MetaLocation == null)
                        continue;

					var map = new MetaMap();
					tagMaps[tag] = map;
					mapsToProcess.Enqueue(map);

					reader.SeekTo(tag.MetaLocation.AsOffset());
					analyzer.AnalyzeArea(reader, tag.MetaLocation.AsPointer(), map);
				}
				GenerateSubMaps(mapsToProcess, analyzer, reader, cacheFile);

				var classMaps = new Dictionary<string, MetaMap>();
				foreach (var tag in cacheFile.Tags)
				{
                    if (tag.MetaLocation == null)
                        continue;

					var map = tagMaps[tag];
                    EstimateMapSize(map, tag.MetaLocation.AsPointer(), analyzer.GeneratedMemoryMap, 1);

					var magicStr = CharConstant.ToString(tag.Class.Magic);
					MetaMap oldClassMap;
					if (classMaps.TryGetValue(magicStr, out oldClassMap))
						oldClassMap.MergeWith(map);
					else
						classMaps[magicStr] = map;
				}

				foreach (var map in classMaps)
				{
					MetaMap globalMap;
					if (globalMaps.TryGetValue(map.Key, out globalMap))
						globalMap.MergeWith(map.Value);
					else
						globalMaps[map.Key] = map.Value;
				}

				reader.Close();

				worker.ReportProgress(100 * i / (generatorMaps.Count - 1));
			}

			var badChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
			foreach (var map in globalMaps)
			{
				var filename = badChars.Aggregate(map.Key, (current, badChar) => current.Replace(badChar, '_'));
				filename += ".xml";
				var path = Path.Combine(outputPath, filename);

				var settings = new XmlWriterSettings
					               {
						               Indent = true, 
									   IndentChars = "\t"
					               };
                using (var writer = XmlWriter.Create(path, settings))
                {
                    var pluginWriter = new AssemblyPluginWriter(writer, gameIdentifier);

                    var size = map.Value.GetBestSizeEstimate();
                    FoldSubMaps(map.Value);

                    pluginWriter.EnterPlugin(size);

                    pluginWriter.EnterRevisions();
                    pluginWriter.VisitRevision(new PluginRevision("Assembly", 1, "Generated plugin from scratch."));
                    pluginWriter.LeaveRevisions();

                    WritePlugin(map.Value, size, pluginWriter);
                    pluginWriter.LeavePlugin();
                }
			}

			var endTime = DateTime.Now;
			e.Result = endTime.Subtract(startTime);
		}

		private void WritePlugin(MetaMap map, int size, IPluginVisitor writer)
		{
			for (var offset = 0; offset < size; offset += 4)
			{
				var guess = map.GetGuess(offset);
				if (guess != null)
				{
					switch (guess.Type)
					{
						case MetaValueType.DataReference:
							if (offset <= size - 0x14)
							{
								writer.VisitDataReference("Unknown", (uint)offset, "bytes", false, 0);
								offset += 0x10;
								continue;
							}
							break;

						case MetaValueType.TagReference:
							if (offset <= size - 0x10)
							{
								writer.VisitTagReference("Unknown", (uint)offset, false, true, true, 0);
								offset += 0xC;
								continue;
							}
							break;

						case MetaValueType.Reflexive:
							if (offset <= size - 0xC)
							{
								var subMap = map.GetSubMap(offset);
								if (subMap != null)
								{
									var subMapSize = subMap.GetBestSizeEstimate();
									writer.EnterReflexive("Unknown", (uint)offset, false, (uint)subMapSize, 0);
									WritePlugin(subMap, subMapSize, writer);
									writer.LeaveReflexive();
									offset += 0x8;
									continue;
								}
							}
							break;
					}
				}

				// Just write an unknown value depending upon how much space we have left
				if (offset <= size - 4)
					writer.VisitUndefined("Unknown", (uint)offset, false, 0);
				else if (offset <= size - 2)
					writer.VisitInt16("Unknown", (uint)offset, false, 0);
				else
					writer.VisitInt8("Unknown", (uint)offset, false, 0);
			}
		}
	    private void GenerateSubMaps(Queue<MetaMap> maps, MetaAnalyzer analyzer, IReader reader, ICacheFile cacheFile)
		{
			var generatedMaps = new Dictionary<uint, MetaMap>();
			while (maps.Count > 0)
			{
				var map = maps.Dequeue();
				foreach (var guess in map.Guesses.Where(guess => guess.Type == MetaValueType.Reflexive))
				{
					MetaMap subMap;
					if (!generatedMaps.TryGetValue(guess.Pointer, out subMap))
					{
						subMap = new MetaMap();
						reader.SeekTo(cacheFile.MetaArea.PointerToOffset(guess.Pointer));
						analyzer.AnalyzeArea(reader, guess.Pointer, subMap);
						maps.Enqueue(subMap);
						generatedMaps[guess.Pointer] = subMap;
					}
					map.AssociateSubMap(guess.Offset, subMap);
				}
			}
		}
		private void EstimateMapSize(MetaMap map, uint mapAddress, MemoryMap memMap, int entryCount)
		{
			var alreadyVisited = map.HasSizeEstimates;

			var newSize = memMap.EstimateBlockSize(mapAddress);
			map.EstimateSize(newSize / entryCount);
			map.Truncate(newSize);

			if (alreadyVisited) return;

			foreach (var guess in map.Guesses)
			{
				if (guess.Type != MetaValueType.Reflexive) continue;

				var subMap = map.GetSubMap(guess.Offset);
				if (subMap != null)
					EstimateMapSize(subMap, guess.Pointer, memMap, (int)guess.Data1);
			}
		}
		private void FoldSubMaps(MetaMap map)
		{
			foreach (var guess in map.Guesses)
			{
				if (guess.Type != MetaValueType.Reflexive) continue;

				var subMap = map.GetSubMap(guess.Offset);
				if (subMap == null) continue;

				//var entryCount = (int)guess.Data1;
				var firstBlockSize = subMap.GetBestSizeEstimate();
				//if (firstBlockSize > 0 && !subMap.IsFolded(firstBlockSize))
				//{
				subMap.Fold(firstBlockSize);
				FoldSubMaps(subMap);
				//}
			}
		}
		private KeyValuePair<ICacheFile, BuildInformation> LoadMap(string path, out IReader reader)
		{
			reader = new EndianReader(File.OpenRead(path), Endian.BigEndian);
			var versionInfo = new CacheFileVersionInfo(reader);
			var buildInfo = _buildLoader.LoadBuild(versionInfo.BuildString);

			return
				new KeyValuePair<ICacheFile, BuildInformation>(new ThirdGenCacheFile(reader, buildInfo, versionInfo.BuildString),
				                                               buildInfo);
		}
    }
}
