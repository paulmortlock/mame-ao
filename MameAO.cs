﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.IO;
using System.Data;
using System.IO.Compression;
using System.Diagnostics;
using System.Xml.Linq;
using System.Reflection;

using System.Data.SQLite;
using Newtonsoft.Json;

namespace Spludlow.MameAO
{
	public class MameAOProcessor
	{
		private readonly HttpClient _HttpClient;

		private readonly string _RootDirectory;
		private string _VersionDirectory;
		private string _Version;

		private bool _LinkingEnabled = false;

		private Dictionary<string, XElement> _MachineElements;
		private Dictionary<string, XElement> _SoftwareListElements;

		public SQLiteConnection _MachineConnection;
		public SQLiteConnection _SoftwareConnection;

		private Spludlow.HashStore _RomHashStore;
		private Spludlow.HashStore _DiskHashStore;

		private string _DownloadTempDirectory;

		private MameChdMan _MameChdMan;

		private readonly long _DownloadDotSize = 1024 * 1024;

		public readonly string _ListenAddress = "http://127.0.0.1:12380/";

		public readonly char _h1 = '#';
		public readonly char _h2 = '.';

		public enum MameSetType
		{
			MachineRom,
			MachineDisk,
			SoftwareRom,
			SoftwareDisk,
		};

		public class MameSourceSet
		{
			public MameSetType SetType;
			public string MetadataUrl;
			public string DownloadUrl;
			public string HtmlSizesUrl;
			public Dictionary<string, long> AvailableDownloadSizes;
		}

		private readonly string _BinariesDownloadUrl = "https://github.com/mamedev/mame/releases/download/mame@VERSION@/mame@VERSION@b_64bit.exe";

		private readonly MameSourceSet[] _MameSourceSets = new MameSourceSet[] {
			new MameSourceSet
			{
				SetType = MameSetType.MachineRom,
				MetadataUrl = "https://archive.org/metadata/mame-merged",
				DownloadUrl = "https://archive.org/download/mame-merged/mame-merged/@MACHINE@.zip",
				HtmlSizesUrl = null,
			},
			new MameSourceSet
			{
				SetType = MameSetType.MachineDisk,
				MetadataUrl = "https://archive.org/metadata/mame-chds-roms-extras-complete",
				DownloadUrl = "https://archive.org/download/mame-chds-roms-extras-complete/@MACHINE@/@DISK@.chd",
				HtmlSizesUrl = null,
			},
			new MameSourceSet
			{
				SetType = MameSetType.SoftwareRom,
				MetadataUrl = "https://archive.org/metadata/mame-sl",
				DownloadUrl = "https://archive.org/download/mame-sl/mame-sl/@LIST@.zip/@LIST@%2f@SOFTWARE@.zip",
				HtmlSizesUrl = "https://archive.org/download/mame-sl/mame-sl/@LIST@.zip/",
			},
			new MameSourceSet
			{
				SetType = MameSetType.SoftwareDisk,
				MetadataUrl = "https://archive.org/metadata/mame-software-list-chds-2",
				DownloadUrl = "https://archive.org/download/mame-software-list-chds-2/@LIST@/@SOFTWARE@/@DISK@.chd",
				HtmlSizesUrl = null,
			},
		};

		private HashSet<string> machineTables = new HashSet<string>(new string[] {
			"mame",
			"machine",
			"rom",
			"device_ref",
			//"sample",
			//"chip",
			//"display",
			//"sound",
			//"input",
			//"control",
			//"dipswitch",
			//"diplocation",
			//"dipvalue",
			//"port",
			"driver",
			"feature",
			//"biosset",
			//"dipswitch_condition",
			//"analog",
			//"configuration",
			//"confsetting",
			//"device",
			//"instance",
			//"extension",
			//"slot",
			"softwarelist",
			//"dipvalue_condition",
			//"slotoption",
			//"adjuster",
			"disk",
			//"ramoption",
			//"configuration_condition",
			//"confsetting_condition",
			//"conflocation",
		});

		private HashSet<string> softwareTables = new HashSet<string>(new string[] {
			"softwarelists",
			"softwarelist",
			"software",
			//"info",
			"part",
			//"feature",
			"dataarea",
			"rom",
			//"sharedfeat",
			"diskarea",
			"disk",
		});

		private MameSourceSet[] GetSourceSets(MameSetType type)
		{
			MameSourceSet[] results =
				(from sourceSet in _MameSourceSets
				where sourceSet.SetType == type
				select sourceSet).ToArray();

			if (results.Length == 0)
				throw new ApplicationException($"Did not find any source sets: {type}");

			return results;
		}

		public MameAOProcessor(string rootDirectory)
		{
			_RootDirectory = rootDirectory;
			_HttpClient = new HttpClient();
		}

		public void Run()
		{
			try
			{
				Initialize();
				Shell();
			}
			catch (Exception ee)
			{
				Console.WriteLine();
				Console.WriteLine("!!! FATAL ERROR: " + ee.Message);
				Console.WriteLine();
				Console.WriteLine(ee.ToString());
				Console.WriteLine();
				Console.WriteLine("Press any key to continue, program has crashed and will exit.");
				Console.WriteLine("If you want to submit an error report please copy and paste the text from here.");
				Console.WriteLine("Select All (Ctrl+A) -> Copy (Ctrl+C) -> notepad -> paste (Ctrl+V)");

				Console.ReadKey();
				Environment.Exit(1);
			}
		}

		public void Initialize()
		{
			Version assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

			Tools.ConsoleHeading(_h1, new string[] {
				$"Welcome to Spludlow MAME Shell V{assemblyVersion.Major}.{assemblyVersion.Minor}",
				"https://github.com/sam-ludlow/mame-ao",
			});
			Console.WriteLine("");
			Console.WriteLine("Give it a moment the first time you run");
			Console.WriteLine("");
			Console.WriteLine("Usage: type the MAME machine name and press enter e.g. \"mrdo\"");
			Console.WriteLine("       or a CHD e.g. \"gauntleg\"");
			Console.WriteLine("       or with MAME arguments e.g. \"mrdo -window\"");
			Console.WriteLine("");
			Console.WriteLine("SL usage: type the MAME machine name and the software name and press enter e.g. \"a2600 et\"");
			Console.WriteLine("       or a CHD e.g. \"cdimono1 aidsawar\" (Note: Not all CHD SLs are not currently available)");
			Console.WriteLine("       or with MAME arguments e.g. \"a2600 et -window\"");
			Console.WriteLine("");
			Console.WriteLine("Use dot to run mame without a machine e.g. \".\", or with paramters \". -window\"");
			Console.WriteLine("If you have alreay loaded a machine (in current MAME version) you can use the MAME UI, filter on avaialable.");
			Console.WriteLine("");
			Console.WriteLine("WARNING: Large downloads like CHD will take a while, each dot represents 1 MiB (about a floppy disk) you do the maths.");
			Console.WriteLine("");

			Tools.ConsoleHeading(_h1, "Initializing");
			Console.WriteLine("");

			//
			// Root Directory
			//
			Console.WriteLine($"Data Directory: {_RootDirectory}");
			Console.WriteLine("");

			//
			// Symbolic Links check
			//

			string linkFilename = Path.Combine(_RootDirectory, @"_LINK_TEST.txt");
			string targetFilename = Path.Combine(_RootDirectory, @"_TARGET_TEST.txt");

			if (File.Exists(linkFilename) == true)
				File.Delete(linkFilename);
			File.WriteAllText(targetFilename, "TEST");

			Tools.LinkFiles(new string[][] { new string[] { linkFilename, targetFilename } });

			_LinkingEnabled = File.Exists(linkFilename);

			if (File.Exists(linkFilename) == true)
				File.Delete(linkFilename);
			if (File.Exists(targetFilename) == true)
				File.Delete(targetFilename);

			Console.WriteLine($"Symbolic Links Enabled: {_LinkingEnabled}");
			if (_LinkingEnabled == false)
				Console.WriteLine("!!! You can save a lot of disk space by enabling symbolic links, see the README.");
			Console.WriteLine();

			//
			// Prepare sources
			//

			foreach (MameSetType setType in Enum.GetValues(typeof(MameSetType)))
			{
				Tools.ConsoleHeading(_h2, $"Prepare source: {setType}");

				MameSourceSet sourceSet = GetSourceSets(setType)[0];

				string setTypeName = setType.ToString();
				string metadataFilename = Path.Combine(_RootDirectory, $"_metadata_{setTypeName}.json");

				dynamic metadata = GetArchiveOrgMeteData(setTypeName, sourceSet.MetadataUrl, metadataFilename);

				string title = metadata.metadata.title;
				string version = "";

				switch (setType)
				{
					case MameSetType.MachineRom:
						version = title.Substring(5, 5);

						sourceSet.AvailableDownloadSizes = AvailableFilesInMetadata("mame-merged/", metadata);
						break;

					case MameSetType.MachineDisk:
						version = title.Substring(5, 5);

						sourceSet.AvailableDownloadSizes = AvailableDiskFilesInMetadata(metadata);
						break;

					case MameSetType.SoftwareRom:
						version = title.Substring(8, 5);

						sourceSet.AvailableDownloadSizes = AvailableFilesInMetadata("mame-sl/", metadata);
						break;

					case MameSetType.SoftwareDisk:
						version = title;

						sourceSet.AvailableDownloadSizes = AvailableDiskFilesInMetadata(metadata);
						break;
				}

				version = version.Replace(".", "").Trim();

				Console.WriteLine($"Title:\t{title}");
				Console.WriteLine($"Version:\t{version}");

				if (setType == MameSetType.MachineRom)
				{
					_Version = version;

					_VersionDirectory = Path.Combine(_RootDirectory, _Version);

					if (Directory.Exists(_VersionDirectory) == false)
					{
						Console.WriteLine($"New MAME version: {_Version}");
						Directory.CreateDirectory(_VersionDirectory);
					}
				}
				else
				{
					if (setType != MameSetType.SoftwareDisk)	//	Source not kept up to date, like others (pot luck)
					{
						if (_Version != version)
							Console.WriteLine($"!!! {setType} on archive.org version mismatch, expected:{_Version} got:{version}. You may have problems.");
					}
				}
			}

			//
			// MAME Binaries
			//

			string binUrl = _BinariesDownloadUrl.Replace("@VERSION@", _Version);

			Tools.ConsoleHeading(_h2, new string[] {
				"MAME",
				binUrl,
			});

			string binCacheFilename = Path.Combine(_VersionDirectory, "_" + Path.GetFileName(binUrl));

			string binFilename = Path.Combine(_VersionDirectory, "mame.exe");

			if (Directory.Exists(_VersionDirectory) == false)
			{
				Console.WriteLine($"New MAME version: {_Version}");
				Directory.CreateDirectory(_VersionDirectory);
			}

			if (File.Exists(binCacheFilename) == false)
			{
				Console.Write($"Downloading MAME binaries {binUrl} ...");
				Tools.Download(binUrl, binCacheFilename, _DownloadDotSize, 10);
				Console.WriteLine("...done.");
			}

			if (File.Exists(binFilename) == false)
			{
				Console.Write($"Extracting MAME binaries {binFilename} ...");
				RunSelfExtract(binCacheFilename);
				Console.WriteLine("...done.");
			}

			//
			// CHD Manager
			//

			_MameChdMan = new MameChdMan(_VersionDirectory);

			//
			// Hash Stores
			//

			string directory = Path.Combine(_RootDirectory, "_STORE");
			if (Directory.Exists(directory) == false)
				Directory.CreateDirectory(directory);
			_RomHashStore = new HashStore(directory, Tools.SHA1HexFile);

			directory = Path.Combine(_RootDirectory, "_STORE_DISK");
			
			if (Directory.Exists(directory) == false)
				Directory.CreateDirectory(directory);
			_DiskHashStore = new HashStore(directory, _MameChdMan.Hash);

			directory = Path.Combine(_RootDirectory, "_TEMP");
			if (Directory.Exists(directory) == false)
				Directory.CreateDirectory(directory);
			_DownloadTempDirectory = directory;

			//
			// MAME Machine XML & SQL
			//

			XElement machineDoc = ExtractMameXml("machine", "-listxml", binFilename, Path.Combine(_VersionDirectory, "_machine.xml"));

			_MachineElements = new Dictionary<string, XElement>();

			foreach (XElement machine in machineDoc.Elements("machine"))
				_MachineElements.Add(machine.Attribute("name").Value, machine);

			_MachineConnection = Database.DatabaseFromXML(machineDoc, Path.Combine(_VersionDirectory, "_machine.sqlite"), machineTables);

			//
			// MAME Software XML & SQL
			//

			XElement softwareDoc = ExtractMameXml("software", "-listsoftware", binFilename, Path.Combine(_VersionDirectory, "_software.xml"));

			_SoftwareListElements = new Dictionary<string, XElement>();
			foreach (XElement softwarelist in softwareDoc.Elements("softwarelist"))
				_SoftwareListElements.Add(softwarelist.Attribute("name").Value, softwarelist);

			_SoftwareConnection = Database.DatabaseFromXML(softwareDoc, Path.Combine(_VersionDirectory, "_software.sqlite"), softwareTables);

			Console.WriteLine("");
		}

		private Dictionary<string, long> AvailableFilesInMetadata(string find, dynamic metadata)
		{
			Dictionary<string, long> result = new Dictionary<string, long>();

			foreach (dynamic file in metadata.files)
			{
				string name = (string)file.name;
				if (name.StartsWith(find) == true && name.EndsWith(".zip") == true)
				{
					name = name.Substring(find.Length);
					name = name.Substring(0, name.Length - 4);
					long size = Int64.Parse((string)file.size);
					result.Add(name, size);
				}
			}

			return result;
		}

		private Dictionary<string, long> AvailableDiskFilesInMetadata(dynamic metadata)
		{
			Dictionary<string, long> result = new Dictionary<string, long>();

			foreach (dynamic file in metadata.files)
			{
				string name = (string)file.name;
				if (name.EndsWith(".chd") == true)
				{
					long size = Int64.Parse((string)file.size);
					result.Add(name.Substring(0, name.Length - 4), size);
				}
			}

			return result;
		}

		private dynamic GetArchiveOrgMeteData(string name, string metadataUrl, string metadataCacheFilename)
		{
			if (File.Exists(metadataCacheFilename) == false || (DateTime.Now - File.GetLastWriteTime(metadataCacheFilename) > TimeSpan.FromHours(3)))
			{
				Console.Write($"Downloading {name} metadata JSON {metadataUrl} ...");
				File.WriteAllText(metadataCacheFilename, Tools.PrettyJSON(Tools.Query(_HttpClient, metadataUrl)), Encoding.UTF8);
				Console.WriteLine("...done.");
			}

			Console.Write($"Loading {name} metadata JSON {metadataCacheFilename} ...");
			dynamic metadata = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(metadataCacheFilename, Encoding.UTF8));
			Console.WriteLine("...done.");

			return metadata;
		}

		private XElement ExtractMameXml(string name, string argument, string binFilename, string filename)
		{
			if (File.Exists(filename) == false)
			{
				Console.Write($"Extracting MAME {name} XML {filename} ...");
				ExtractXML(binFilename, filename, argument);
				Console.WriteLine("...done.");
			}

			Console.Write($"Loading MAME {name} XML {filename} ...");
			XElement doc = XElement.Load(filename);
			Console.WriteLine("...done.");

			return doc;
		}

		public void Shell()
		{
			WebServer webServer = new WebServer(this);
			webServer.StartListener();

			Tools.ConsoleHeading(_h1, new string[] {
				"Remote Listener ready for commands",
				_ListenAddress,
				$"e.g. {_ListenAddress}command?machine=a2600&software=et&arguments=-window"

			});
			Console.WriteLine("");

			Process.Start(_ListenAddress);

			Tools.ConsoleHeading(_h1, "Shell ready for commands");
			Console.WriteLine("");

			while (true)
			{
				Console.Write($"MAME Shell ({_Version})> ");
				string line = Console.ReadLine();
				line = line.Trim();

				if (line.Length == 0)
					continue;

				RunLine(line);
			}
		}

		public void RunLine(string line)
		{
			string binFilename = Path.Combine(_VersionDirectory, "mame.exe");

			string machine;
			string software = "";
			string arguments = "";

			string[] parts = line.Split(new char[] { ' ' });

			machine = parts[0];

			if (machine == ".")
			{
				if (parts.Length > 1)
					arguments = String.Join(" ", parts.Skip(1));
			}
			else
			{
				if (parts.Length >= 2)
				{
					if (parts[1].StartsWith("-") == false)
					{
						software = parts[1];

						if (parts.Length > 2)
							arguments = String.Join(" ", parts.Skip(2));
					}
					else
					{
						arguments = String.Join(" ", parts.Skip(1));
					}
				}
			}

			machine = machine.ToLower().Trim();
			software = software.ToLower().Trim();

			try
			{
				if (machine.StartsWith(".") == true)
				{
					RunMame(binFilename, arguments);
				}
				else
				{
					GetRoms(machine, software);
					RunMame(binFilename, machine + " " + software + " " + arguments);
				}
			}
			catch (ApplicationException ee)
			{
				Console.WriteLine("SHELL ERROR: " + ee.Message);
				Console.WriteLine();
			}
		}

		public void RunMame(string binFilename, string arguments)
		{
			Tools.ConsoleHeading(_h1, new string[] {
				"Starting MAME",
				binFilename,
				arguments,
			});
			Console.WriteLine();

			string directory = Path.GetDirectoryName(binFilename);

			ProcessStartInfo startInfo = new ProcessStartInfo(binFilename)
			{
				WorkingDirectory = directory,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				StandardOutputEncoding = Encoding.UTF8,
			};

			using (Process process = new Process())
			{
				process.StartInfo = startInfo;

				process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
				{
					if (e.Data != null)
						Console.WriteLine($"MAME output:{e.Data}");
				});

				process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
				{
					if (e.Data != null)
						Console.WriteLine($"MAME error:{e.Data}");
				});

				process.Start();
				process.BeginOutputReadLine();
				process.WaitForExit();

				Console.WriteLine();
				if (process.ExitCode == 0)
					Console.WriteLine("MAME Shell Exit OK.");
				else
					Console.WriteLine($"MAME Shell Exit BAD: {process.ExitCode}");
			}

			Console.WriteLine();
		}

		public XElement GetMachine(string name)
		{
			if (_MachineElements.ContainsKey(name) == false)
				return null;
			return _MachineElements[name];
		}

		public XElement GetSoftwareList(string name)
		{
			if (_SoftwareListElements.ContainsKey(name) == false)
				return null;
			return _SoftwareListElements[name];
		}

		public void GetRoms(string machineName, string softwareName)
		{
			Tools.ConsoleHeading(_h1, "Asset Acquisition");
			Console.WriteLine();

			//
			// Machine
			//
			XElement machine = GetMachine(machineName);
			if (machine == null)
				throw new ApplicationException($"Machine not found: {machineName}");

			XElement[] softwarelists = machine.Elements("softwarelist").ToArray();

			List<string[]> romStoreFilenames = new List<string[]>();

			int missingCount = 0;

			//
			// Machine ROMs
			//
			missingCount += GetRomsMachine(machineName, romStoreFilenames);

			//
			// Machine Disks
			//
			missingCount += GetDisksMachine(machineName, romStoreFilenames);

			//
			// Software ROMSs & Disks
			//
			if (softwareName != "")
			{
				int softwareFound = 0;

				foreach (XElement machineSoftwarelist in softwarelists)
				{
					string softwarelistName = machineSoftwarelist.Attribute("name").Value;

					XElement softwarelist = GetSoftwareList(softwarelistName);
						
					if (softwarelist == null)
						throw new ApplicationException($"Software list on machine but not in SL {softwarelistName}");

					foreach (XElement findSoftware in softwarelist.Elements("software"))
					{
						if (findSoftware.Attribute("name").Value == softwareName)
						{
							missingCount += GetRomsSoftware(findSoftware, romStoreFilenames);

							missingCount += GetDiskSoftware(findSoftware, romStoreFilenames);

							++softwareFound;
						}
					}
				}

				if (softwareFound == 0)
					throw new ApplicationException($"Did not find software: {machineName}, {softwareName}");

				if (softwareFound > 1)
				{
					Console.WriteLine();
					Console.WriteLine("!!! Warning more than one software found, not sure which MAME will use. This can happern if the same name apears in different lists e.g. disk & cassette.");
					Console.WriteLine();
				}
			}

			//
			// Place ROMs
			//
			if (_LinkingEnabled == true)
			{
				Tools.LinkFiles(romStoreFilenames.ToArray());
			}
			else
			{
				foreach (string[] romStoreFilename in romStoreFilenames)
					File.Copy(romStoreFilename[1], romStoreFilename[0]);
			}

			Console.WriteLine();

			//
			// Info
			//
			Tools.ConsoleHeading(_h1, new string[] {
				"Machine Information",
				"",
				missingCount == 0 ? "Everything looks good to run MAME" : "!!! Missing ROM & Disk files. I doubt MAME will run !!!",
			});
			Console.WriteLine();

			XElement[] features = machine.Elements("feature").ToArray();

			Console.WriteLine($"Name:           {machine.Attribute("name").Value}");
			Console.WriteLine($"Description:    {machine.Element("description")?.Value}");
			Console.WriteLine($"Year:           {machine.Element("year")?.Value}");
			Console.WriteLine($"Manufacturer:   {machine.Element("manufacturer")?.Value}");
			Console.WriteLine($"Status:         {machine.Element("driver")?.Attribute("status")?.Value}");
			Console.WriteLine($"isbios:         {machine.Attribute("isbios")?.Value}");
			Console.WriteLine($"isdevice:       {machine.Attribute("isdevice")?.Value}");
			Console.WriteLine($"ismechanical:   {machine.Attribute("ismechanical")?.Value}");
			Console.WriteLine($"runnable:       {machine.Attribute("runnable")?.Value}");

			foreach (XElement feature in features)
				Console.WriteLine($"Feature issue:  {feature.Attribute("type")?.Value} {feature.Attribute("status")?.Value}");
			foreach (XElement softwarelist in softwarelists)
				Console.WriteLine($"Software list:  {softwarelist.Attribute("name")?.Value}");

			Console.WriteLine();
		}

		private int GetRomsMachine(string machineName, List<string[]> romStoreFilenames)
		{
			MameSourceSet soureSet = GetSourceSets(MameSetType.MachineRom)[0];

			//
			// Related/Required machines (parent/bios/devices)
			//
			HashSet<string> requiredMachines = new HashSet<string>();

			FindAllMachines(machineName, requiredMachines);

			if (requiredMachines.Count == 0)
				return 0;

			Tools.ConsoleHeading(_h2, new string[] {
				"Machine ROM",
				$"{machineName}",
				$"required machines: {String.Join(", ", requiredMachines.ToArray())}",
			});

			foreach (string requiredMachineName in requiredMachines)
			{
				XElement requiredMachine = GetMachine(requiredMachineName);
				if (requiredMachine == null)
					throw new ApplicationException("requiredMachine not found: " + requiredMachineName);

				//
				// See if ROMS are in the hash store
				//
				HashSet<string> missingRoms = new HashSet<string>();

				foreach (XElement rom in requiredMachine.Descendants("rom"))
				{
					string romName = rom.Attribute("name")?.Value;
					string sha1 = rom.Attribute("sha1")?.Value;

					if (romName == null || sha1 == null)
						continue;

					bool inStore = _RomHashStore.Exists(sha1);

					Console.WriteLine($"Checking machine ROM: {inStore}\t{requiredMachineName}\t{romName}\t{sha1}");

					if (inStore == false)
						missingRoms.Add(sha1);
				}

				//
				// If not then download and import into hash store
				//
				if (missingRoms.Count > 0)
				{
					if (soureSet.AvailableDownloadSizes.ContainsKey(requiredMachineName) == true)
					{
						string downloadMachineUrl = soureSet.DownloadUrl;
						downloadMachineUrl = downloadMachineUrl.Replace("@MACHINE@", requiredMachineName);

						long size = soureSet.AvailableDownloadSizes[requiredMachineName];

						ImportRoms(downloadMachineUrl, $"machine:{requiredMachineName}", size);
					}
				}
			}

			//
			// Check and place ROMs
			//
			int missingCount = 0;

			foreach (string requiredMachineName in requiredMachines)
			{
				XElement requiredMachine = GetMachine(requiredMachineName);
				if (requiredMachine == null)
					throw new ApplicationException("requiredMachine not found: " + requiredMachineName);

				foreach (XElement rom in requiredMachine.Descendants("rom"))
				{
					string romName = rom.Attribute("name")?.Value;
					string sha1 = rom.Attribute("sha1")?.Value;

					if (romName == null || sha1 == null)
						continue;

					string romFilename = Path.Combine(_VersionDirectory, "roms", requiredMachineName, romName);
					string romDirectory = Path.GetDirectoryName(romFilename);
					if (Directory.Exists(romDirectory) == false)
						Directory.CreateDirectory(romDirectory);

					if (_RomHashStore.Exists(sha1) == true)
					{
						if (File.Exists(romFilename) == false)
						{
							Console.WriteLine($"Place machine ROM: {requiredMachineName}\t{romName}");
							romStoreFilenames.Add(new string[] { romFilename, _RomHashStore.Filename(sha1) });
						}
					}
					else
					{
						Console.WriteLine($"Missing machine ROM: {requiredMachineName}\t{romName}\t{sha1}");
						++missingCount;
					}
				}
			}

			return missingCount;
		}

		private int GetDisksMachine(string machineName, List<string[]> romStoreFilenames)
		{
			MameSourceSet soureSet = GetSourceSets(MameSetType.MachineDisk)[0];

			XElement machine = GetMachine(machineName);
			if (machine == null)
				throw new ApplicationException("GetDisksMachine machine not found: " + machineName);

			XElement[] disks = machine.Elements("disk").ToArray();

			if (disks.Length == 0)
				return 0;

			Tools.ConsoleHeading(_h2, new string[] {
				"Machine Disk",
				$"{machineName}",
			});

			//
			// See if Disks are in the hash store
			//
			HashSet<XElement> missingDisks = new HashSet<XElement>();

			foreach (XElement disk in disks)
			{
				string name = disk.Attribute("name")?.Value;
				string sha1 = disk.Attribute("sha1")?.Value;

				if (disk == null || sha1 == null)
					continue;

				bool inStore = _DiskHashStore.Exists(sha1);

				Console.WriteLine($"Checking machine Disk: {inStore}\t{machineName}\t{name}\t{sha1}");

				if (inStore == false)
					missingDisks.Add(disk);
			}

			//
			// If not then download and import into hash store
			//
			if (missingDisks.Count > 0)
			{
				string parentMachineName = machine.Attribute("romof")?.Value;

				foreach (XElement disk in missingDisks)
				{
					string diskName = disk.Attribute("name")?.Value;
					string sha1 = disk.Attribute("sha1")?.Value;

					string merge = disk.Attribute("merge")?.Value;

					string availableMachineName = machineName;
					string availableDiskName = diskName;

					if (merge != null)
					{
						availableMachineName = parentMachineName ?? throw new ApplicationException($"machine disk merge without parent {machineName}");
						availableDiskName = merge;
					}

					string key = $"{availableMachineName}/{availableDiskName}";

					if (soureSet.AvailableDownloadSizes.ContainsKey(key) == false)
					{
						availableMachineName = parentMachineName;
						key = $"{availableMachineName}/{availableDiskName}";

						if (soureSet.AvailableDownloadSizes.ContainsKey(key) == false)
							throw new ApplicationException($"Available Download Machine Disks not found key:{key}");
					}

					long size = soureSet.AvailableDownloadSizes[key];

					string diskNameEnc = Uri.EscapeUriString(availableDiskName);

					string diskUrl = soureSet.DownloadUrl;
					diskUrl = diskUrl.Replace("@MACHINE@", availableMachineName);
					diskUrl = diskUrl.Replace("@DISK@", diskNameEnc);

					string tempFilename = Path.Combine(_DownloadTempDirectory, DateTime.Now.ToString("s").Replace(":", "-") + "_" + availableDiskName);

					Console.Write($"Downloading {key} Machine Disk. size:{Tools.DataSize(size)} url:{diskUrl} ...");
					long downloadSize = Tools.Download(diskUrl, tempFilename, _DownloadDotSize, 3 * 60);
					Console.WriteLine($"...done");

					if (size != downloadSize)
						Console.WriteLine($"Unexpected downloaded file size expect:{size} actual:{downloadSize}");

					bool imported = _DiskHashStore.Add(tempFilename, true);

					Console.WriteLine($"Disk Store Import: {imported} {key}");
				}
			}

			//
			// Check and place
			//

			int missing = 0;

			foreach (XElement disk in disks)
			{
				string name = disk.Attribute("name")?.Value;
				string sha1 = disk.Attribute("sha1")?.Value;

				if (disk == null || sha1 == null)
					continue;

				string filename = Path.Combine(_VersionDirectory, "roms", machineName, name + ".chd");
				string directory = Path.GetDirectoryName(filename);

				if (Directory.Exists(directory) == false)
					Directory.CreateDirectory(directory);

				if (_DiskHashStore.Exists(sha1) == true)
				{
					if (File.Exists(filename) == false)
					{
						Console.WriteLine($"Place machine Disk: {machineName}\t{name}");
						romStoreFilenames.Add(new string[] { filename, _DiskHashStore.Filename(sha1) });
					}
				}
				else
				{
					Console.WriteLine($"Missing machine Disk: {machineName}\t{name}\t{sha1}");
					++missing;
				}
			}

			return missing;
		}

		private int GetDiskSoftware(XElement software, List<string[]> romStoreFilenames)
		{
			MameSourceSet soureSet = GetSourceSets(MameSetType.SoftwareDisk)[0];

			XElement softwareList = software.Parent;

			string softwareListName = softwareList.Attribute("name").Value;
			string softwareName = software.Attribute("name").Value;

			IEnumerable<XElement> disks = software.Descendants("disk");
			int diskCount = disks.Count();

			if (diskCount == 0)
				return 0;

			Tools.ConsoleHeading(_h2, new string[] {
				"Software Disk",
				$"{softwareListName} / {softwareName}",
			});

			//
			// See if Disks are in the hash store
			//
			HashSet<XElement> missingDisks = new HashSet<XElement>();

			foreach (XElement disk in disks)
			{
				string name = disk.Attribute("name")?.Value;
				string sha1 = disk.Attribute("sha1")?.Value;

				if (disk == null || sha1 == null)
					continue;

				bool inStore = _DiskHashStore.Exists(sha1);

				Console.WriteLine($"Checking software Disk: {inStore}\t{softwareListName}\t{softwareName}\t{name}\t{sha1}");

				if (inStore == false)
					missingDisks.Add(disk);
			}

			//
			// If not then download and import into hash store
			//
			if (missingDisks.Count > 0)
			{
				string downloadSoftwareName = softwareName;
				string parentSoftwareName = software.Attribute("cloneof")?.Value;
				if (parentSoftwareName != null)
					downloadSoftwareName = parentSoftwareName;

				foreach (XElement disk in missingDisks)
				{
					string diskName = disk.Attribute("name")?.Value;
					string sha1 = disk.Attribute("sha1")?.Value;

					string key = $"{softwareListName}/{downloadSoftwareName}/{diskName}";

					if (soureSet.AvailableDownloadSizes.ContainsKey(key) == false)
						throw new ApplicationException($"Software list disk not on archive.org {key}");

					long size = soureSet.AvailableDownloadSizes[key];

					string nameEnc = Uri.EscapeUriString(diskName);

					string url = soureSet.DownloadUrl;
					url = url.Replace("@LIST@", softwareListName);
					url = url.Replace("@SOFTWARE@", downloadSoftwareName);
					url = url.Replace("@DISK@", nameEnc);

					string tempFilename = Path.Combine(_DownloadTempDirectory, DateTime.Now.ToString("s").Replace(":", "-") + "_" + diskName);

					Console.Write($"Downloading {key} Software Disk. size:{Tools.DataSize(size)} url:{url} ...");
					long downloadSize = Tools.Download(url, tempFilename, _DownloadDotSize, 3 * 60);
					Console.WriteLine($"...done");

					if (size != downloadSize)
						Console.WriteLine($"Unexpected downloaded file size expect:{size} actual:{downloadSize}");

					bool imported = _DiskHashStore.Add(tempFilename, true);

					Console.WriteLine($"Disk Store Import: {imported} {key}");
				}
			}

			//
			// Check and place
			//

			int missing = 0;

			foreach (XElement disk in disks)
			{
				string name = disk.Attribute("name")?.Value;
				string sha1 = disk.Attribute("sha1")?.Value;

				if (disk == null || sha1 == null)
					continue;

				string filename = Path.Combine(_VersionDirectory, "roms", softwareListName, softwareName, name + ".chd");
				string directory = Path.GetDirectoryName(filename);

				if (Directory.Exists(directory) == false)
					Directory.CreateDirectory(directory);

				if (_DiskHashStore.Exists(sha1) == true)
				{
					if (File.Exists(filename) == false)
					{
						Console.WriteLine($"Place software Disk: {softwareListName}\t{softwareName}\t{name}");
						romStoreFilenames.Add(new string[] { filename, _DiskHashStore.Filename(sha1) });
					}
				}
				else
				{
					Console.WriteLine($"Missing softwsre Disk: {softwareListName}\t{softwareName}\t{name}\t{sha1}");
					++missing;
				}
			}

			return missing;
		}

		private int GetRomsSoftware(XElement software, List<string[]> romStoreFilenames)
		{
			MameSourceSet soureSet = GetSourceSets(MameSetType.SoftwareRom)[0];

			XElement softwareList = software.Parent;

			string softwareListName = softwareList.Attribute("name").Value;
			string softwareName = software.Attribute("name").Value;

			IEnumerable<XElement> roms = software.Descendants("rom");
			int romCount = roms.Count();

			if (romCount == 0)
				return 0;

			Tools.ConsoleHeading(_h2, new string[] {
				"Software ROM",
				$"{softwareListName} / {softwareName}",
			});

			if (soureSet.AvailableDownloadSizes.ContainsKey(softwareListName) == false)
				throw new ApplicationException($"Software list not on archive.org {softwareListName}");

			//
			// Check ROMs in store on SHA1
			//
			HashSet<string> missingRoms = new HashSet<string>();

			foreach (XElement rom in roms)
			{
				string romName = rom.Attribute("name")?.Value;
				string sha1 = rom.Attribute("sha1")?.Value;

				if (romName == null || sha1 == null)
					continue;

				bool inStore = _RomHashStore.Exists(sha1);

				Console.WriteLine($"Checking Software ROM: {inStore}\t{softwareListName}\t{softwareName}\t{romName}\t{sha1}");

				if (inStore == false)
					missingRoms.Add(sha1);
			}

			//
			// Download ROMs and import to store
			//
			if (missingRoms.Count > 0)
			{
				string requiredSoftwareName = softwareName;
				string parentSoftwareName = software.Attribute("cloneof")?.Value;
				if (parentSoftwareName != null)
					requiredSoftwareName = parentSoftwareName;

				string listEnc = Uri.EscapeUriString(softwareListName);
				string softEnc = Uri.EscapeUriString(requiredSoftwareName);

				string downloadSoftwareUrl = soureSet.DownloadUrl;
				downloadSoftwareUrl = downloadSoftwareUrl.Replace("@LIST@", listEnc);
				downloadSoftwareUrl = downloadSoftwareUrl.Replace("@SOFTWARE@", softEnc);

				Dictionary<string, long> softwareSizes = GetSoftwareSizes(softwareListName, soureSet.HtmlSizesUrl);

				if (softwareSizes.ContainsKey(requiredSoftwareName) == false)
					throw new ApplicationException($"Did GetSoftwareSize {softwareListName}, {requiredSoftwareName} ");

				long size = softwareSizes[requiredSoftwareName];

				ImportRoms(downloadSoftwareUrl, $"software:{softwareListName}, {requiredSoftwareName}", size);
			}

			//
			// Check and place ROMs
			//

			int missingCount = 0;
			foreach (XElement rom in roms)
			{
				string romName = rom.Attribute("name")?.Value;
				string sha1 = rom.Attribute("sha1")?.Value;

				if (romName == null || sha1 == null)
					continue;

				string romFilename = Path.Combine(_VersionDirectory, "roms", softwareListName, softwareName, romName);
				string romDirectory = Path.GetDirectoryName(romFilename);
				if (Directory.Exists(romDirectory) == false)
					Directory.CreateDirectory(romDirectory);

				if (_RomHashStore.Exists(sha1) == true)
				{
					if (File.Exists(romFilename) == false)
					{
						Console.WriteLine($"Place software ROM: {softwareListName}\t{softwareName}\t{romName}");
						romStoreFilenames.Add(new string[] { romFilename, _RomHashStore.Filename(sha1) });
					}
				}
				else
				{
					Console.WriteLine($"Missing software ROM: {softwareListName}\t{softwareName}\t{romName}\t{sha1}");
					++missingCount;
				}
			}

			return missingCount;
		}

		private Dictionary<string, long> GetSoftwareSizes(string listName, string htmlSizesUrl)
		{
			string cacheDirectory = Path.Combine(_VersionDirectory, "_SoftwareSizes");
			if (Directory.Exists(cacheDirectory) == false)
				Directory.CreateDirectory(cacheDirectory);

			string filename = Path.Combine(cacheDirectory, listName + ".htm");

			string html;
			if (File.Exists(filename) == false)
			{
				string url = htmlSizesUrl.Replace("@LIST@", listName);
				html = Tools.Query(_HttpClient, url);
				File.WriteAllText(filename, html, Encoding.UTF8);
			}
			else
			{
				html = File.ReadAllText(filename, Encoding.UTF8);
			}

			Dictionary<string, long> result = new Dictionary<string, long>();

			using (StringReader reader = new StringReader(html))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					line = line.Trim();
					if (line.StartsWith("<tr><td><a href=\"//archive.org/download/") == false)
						continue;

					string[] parts = line.Split(new char[] { '<' });

					string name = null;
					string size = null;

					foreach (string part in parts)
					{
						int index = part.LastIndexOf(">");
						if (index == -1)
							continue;
						++index;

						if (part.StartsWith("a href=") == true)
						{
							name = part.Substring(index + listName.Length + 1);
							name = name.Substring(0, name.Length - 4);
						}

						if (part.StartsWith("td id=\"size\"") == true)
							size = part.Substring(index);
					}

					if (name == null || size == null)
						throw new ApplicationException($"Bad html line {listName} {line}");

					result.Add(name, Int64.Parse(size));
				}
			}

			return result;
		}

		private long ImportRoms(string url, string name, long expectedSize)
		{
			long size = 0;

			using (TempDirectory tempDir = new TempDirectory())
			{
				string archiveFilename = Path.Combine(tempDir.Path, "archive.zip");
				string extractDirectory = Path.Combine(tempDir.Path, "OUT");
				Directory.CreateDirectory(extractDirectory);

				Console.Write($"Downloading {name} ROM ZIP size:{Tools.DataSize(expectedSize)} url:{url} ...");
				DateTime startTime = DateTime.Now;
				size = Tools.Download(url, archiveFilename, _DownloadDotSize, 30);
				TimeSpan took = DateTime.Now - startTime;
				Console.WriteLine($"...done");

				if (size != expectedSize)
					Console.WriteLine($"Unexpected downloaded file size expect:{expectedSize} actual:{size}");

				decimal mbPerSecond = (size / (decimal)took.TotalSeconds) / (1024.0M * 1024.0M);

				Console.WriteLine($"Download rate: {Math.Round(took.TotalSeconds, 3)}s = {Math.Round(mbPerSecond, 3)} MiB/s");

				Console.Write($"Extracting {name} ROM ZIP {archiveFilename} ...");
				ZipFile.ExtractToDirectory(archiveFilename, extractDirectory);
				Console.WriteLine($"...done");

				Tools.ClearAttributes(tempDir.Path);

				foreach (string romFilename in Directory.GetFiles(extractDirectory, "*", SearchOption.AllDirectories))
				{
					bool imported = _RomHashStore.Add(romFilename);
					Console.WriteLine($"Store Import: {imported} {name} {romFilename.Substring(extractDirectory.Length)}");
				}
			}

			return size;
		}

		public void FindAllMachines(string machineName, HashSet<string> requiredMachines)
		{
			if (requiredMachines.Contains(machineName) == true)
				return;

			XElement machine = GetMachine(machineName);
			if (machine == null)
				throw new ApplicationException("FindAllMachines machine not found: " + machineName);

			bool hasRoms = (machine.Descendants("rom").Count() > 0);

			if (hasRoms == true)
				requiredMachines.Add(machineName);

			string romof = machine.Attribute("romof")?.Value;

			if (romof != null)
				FindAllMachines(romof, requiredMachines);

			foreach (XElement device_ref in machine.Descendants("device_ref"))
				FindAllMachines(device_ref.Attribute("name").Value, requiredMachines);
		}

		public void RunSelfExtract(string filename)
		{
			string directory = Path.GetDirectoryName(filename);

			ProcessStartInfo startInfo = new ProcessStartInfo(filename)
			{
				WorkingDirectory = directory,
				Arguments = "-y",
			};

			using (Process process = new Process())
			{
				process.StartInfo = startInfo;

				process.Start();
				process.WaitForExit();

				if (process.ExitCode != 0)
					throw new ApplicationException("Bad exit code");
			}
		}

		public void ExtractXML(string binFilename, string outputFilename, string arguments)
		{
			string directory = Path.GetDirectoryName(binFilename);

			using (StreamWriter writer = new StreamWriter(outputFilename, false, Encoding.UTF8))
			{
				ProcessStartInfo startInfo = new ProcessStartInfo(binFilename)
				{
					WorkingDirectory = directory,
					Arguments = arguments,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					StandardOutputEncoding = Encoding.UTF8,
				};

				using (Process process = new Process())
				{
					process.StartInfo = startInfo;

					process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
					{
						if (e.Data != null)
							writer.WriteLine(e.Data);
					});

					process.Start();
					process.BeginOutputReadLine();
					process.WaitForExit();

					if (process.ExitCode != 0)
						throw new ApplicationException("ExtractXML Bad exit code");
				}
			}
		}
	}
}
