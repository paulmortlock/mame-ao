using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace mame_ao.source
{
    public class Database
	{
		public class DataQueryProfile
		{
			public string Key;
			public string Text;
			public string Decription;
			public string CommandText;
		}

		public static DataQueryProfile[] DataQueryProfiles = new DataQueryProfile[] {
			new DataQueryProfile(){
				Key = "arcade-good",
				Text = "Arcade Good",
				Decription = "Arcade Machines - Status Good - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'good') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins > 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "arcade-imperfect",
				Text = "Arcade Imperfect",
				Decription = "Arcade Machines - Status Imperfect - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'imperfect') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins > 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "computer-console-good",
				Text = "Computers & Consoles Good",
				Decription = "Computers & Consoles with software - status good - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'good') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins = 0) AND (ao_softwarelist_count > 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "computer-console-imperfect",
				Text = "Computers & Consoles Imperfect",
				Decription = "Computers & Consoles with software - status imperfect - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'imperfect') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins = 0) AND (ao_softwarelist_count > 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "other-good",
				Text = "Other Good",
				Decription = "Other Systems without software - status good - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'good') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins = 0) AND (ao_softwarelist_count = 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "other-imperfect",
				Text = "Other Imperfect",
				Decription = "Other Systems without software - status imperfect - Parents only",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.cloneof IS NULL) AND (driver.status = 'imperfect') AND (machine.runnable = 'yes') AND (machine.isdevice = 'no') AND (ao_input_coins = 0) AND (ao_softwarelist_count = 0) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "everything",
				Text = "Everything",
				Decription = "Absolutely Everything",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.runnable = 'yes') AND (machine.isdevice = 'no') @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
			new DataQueryProfile(){
				Key = "favorites",
				Text = "Favorites",
				Decription = "Favorites",
				CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((machine.runnable = 'yes') AND (machine.isdevice = 'no') @FAVORITES @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
			},
		};

		public SqlConnection _MachineConnection;
		public SqlConnection _SoftwareConnection;

		private Dictionary<string, DataRow[]> _DevicesRefs;
		private DataTable _SoftwarelistTable;

		public HashSet<string> _AllSHA1s;

		public Database()
		{
		}

		public void InitializeMachine(string xmlFilename, string databaseFilename, string assemblyVersion)
		{
			_MachineConnection = Database.DatabaseFromXML(xmlFilename, databaseFilename, assemblyVersion);

			Console.Write("Creating machine performance caches...");

			// Cache device_ref to speed up machine dependancy resolution
			DataTable device_ref_Table = Database.ExecuteFill(_MachineConnection, "SELECT * FROM device_ref");

			_DevicesRefs = new Dictionary<string, DataRow[]>();

			DataTable machineTable = Database.ExecuteFill(_MachineConnection, "SELECT machine_id, name FROM machine");
			foreach (DataRow row in machineTable.Rows)
			{
				string machineName = (string)row["name"];

				DataRow[] rows = device_ref_Table.Select("machine_id = " + (long)row["machine_id"]);

				_DevicesRefs.Add(machineName, rows);
			}
			
			Console.WriteLine("...done");
		}

		public void InitializeSoftware(string xmlFilename, string databaseFilename, string assemblyVersion)
		{
			_SoftwareConnection = Database.DatabaseFromXML(xmlFilename, databaseFilename, assemblyVersion);

			Console.Write("Creating software performance caches...");

			// Cache softwarelists for description
			_SoftwarelistTable = ExecuteFill(_SoftwareConnection, "SELECT name, description FROM softwarelist");
			_SoftwarelistTable.PrimaryKey = new DataColumn[] { _SoftwarelistTable.Columns["name"] };

			Console.WriteLine("...done");

			Console.Write("Getting all database SHA1s...");
			_AllSHA1s = GetAllSHA1s();
			Console.WriteLine("...done.");
		}

		public static void AddDataExtras(DataSet dataSet, string name, string assemblyVersion)
		{
			DataTable table = new DataTable("ao_info");
			table.Columns.Add("ao_info_id", typeof(long));
			table.Columns.Add("assembly_version", typeof(string));
			table.Rows.Add(1L, assemblyVersion);
			dataSet.Tables.Add(table);

			if (name == "mame")
			{
				DataTable machineTable = dataSet.Tables["machine"];

				DataTable romTable = dataSet.Tables["rom"];
				DataTable diskTable = dataSet.Tables["disk"];
				DataTable softwarelistTable = dataSet.Tables["softwarelist"];
				DataTable driverTable = dataSet.Tables["driver"];
				DataTable inputTable = dataSet.Tables["input"];

				machineTable.Columns.Add("ao_rom_count", typeof(int));
				machineTable.Columns.Add("ao_disk_count", typeof(int));
				machineTable.Columns.Add("ao_softwarelist_count", typeof(int));
				machineTable.Columns.Add("ao_driver_status", typeof(string));
				machineTable.Columns.Add("ao_input_coins", typeof(int));

				foreach (DataRow machineRow in machineTable.Rows)
				{
					long machine_id = (long)machineRow["machine_id"];

					DataRow[] romRows = romTable.Select($"machine_id={machine_id}");
					DataRow[] diskRows = diskTable.Select($"machine_id={machine_id}");
					DataRow[] softwarelistRows = softwarelistTable.Select($"machine_id={machine_id}");
					DataRow[] driverRows = driverTable.Select($"machine_id={machine_id}");
					DataRow[] inputRows = inputTable.Select($"machine_id={machine_id}");

					machineRow["ao_rom_count"] = romRows.Count(row => row.IsNull("sha1") == false);
					machineRow["ao_disk_count"] = diskRows.Count(row => row.IsNull("sha1") == false);

					machineRow["ao_softwarelist_count"] = softwarelistRows.Length;
					if (driverRows.Length == 1)
						machineRow["ao_driver_status"] = (string)driverRows[0]["status"];

					machineRow["ao_input_coins"] = 0;
					if (inputRows.Length == 1 && inputRows[0].IsNull("coins") == false)
						machineRow["ao_input_coins"] = Int32.Parse((string)inputRows[0]["coins"]);

				}
			}

			if (name == "softwarelists")
			{

			}
		}

		public DataRow GetMachine(string name)
		{
			DataTable table = ExecuteFill(_MachineConnection, $"SELECT * FROM machine WHERE name = '{name}'");
			if (table.Rows.Count == 0)
				return null;
			return table.Rows[0];
		}

		public DataRow[] GetMachineRoms(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			DataTable table = Database.ExecuteFill(_MachineConnection, $"SELECT * FROM rom WHERE machine_id = {machine_id} AND [name] IS NOT NULL AND [sha1] IS NOT NULL");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetMachineDisks(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			DataTable table = Database.ExecuteFill(_MachineConnection, $"SELECT * FROM disk WHERE machine_id = {machine_id} AND [name] IS NOT NULL AND [sha1] IS NOT NULL");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetMachineSoftwareLists(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			string machineName = (string)machine["name"];
			DataTable table = ExecuteFill(_MachineConnection, $"SELECT * FROM softwarelist WHERE machine_id = {machine_id}");

			table.Columns.Add("description", typeof(string));

			foreach (DataRow row in table.Rows)
			{
				string listName = (string)row["name"];
				DataRow softwarelistRow = _SoftwarelistTable.Rows.Find(listName);

				if (softwarelistRow != null)
					row["description"] = (string)softwarelistRow["description"];
				else
					row["description"] = $"MAME DATA ERROR machine '{machineName}' software list '{listName}' does not exist";
			}

			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetMachineFeatures(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			DataTable table = ExecuteFill(_MachineConnection, $"SELECT * FROM feature WHERE machine_id = {machine_id}");
			return table.Rows.Cast<DataRow>().ToArray();
		}
		public DataRow[] GetMachineDeviceRefs(string machineName)
		{
			return _DevicesRefs[machineName];
		}

		public DataRow[] GetMachineSamples(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			DataTable table = ExecuteFill(_MachineConnection, $"SELECT * FROM sample WHERE machine_id = {machine_id}");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow GetMachineDriver(DataRow machine)
		{
			long machine_id = (long)machine["machine_id"];
			DataTable table = ExecuteFill(_MachineConnection, $"SELECT * FROM driver WHERE machine_id = {machine_id}");

			if (table.Rows.Count == 0)
				return null;

			return table.Rows[0];
		}

		public DataRow[] GetSoftwareListsSoftware(DataRow softwarelist)
		{
			long softwarelist_id = (long)softwarelist["softwarelist_id"];
			DataTable table = ExecuteFill(_SoftwareConnection, $"SELECT * FROM software WHERE softwarelist_id = {softwarelist_id}");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetSoftwareListsSoftware(string softwareListName, int offset, int limit, string search, string favorites_machine)
		{
			string commandText = "SELECT software.*, softwarelist.name AS softwarelist_name, COUNT() OVER() AS ao_total FROM softwarelist INNER JOIN software ON softwarelist.softwarelist_Id = software.softwarelist_Id " +
				$"WHERE (softwarelist.name = '{softwareListName}' @SEARCH) ORDER BY software.description COLLATE NOCASE ASC " +
				"LIMIT @LIMIT OFFSET @OFFSET";

			if (softwareListName == "@fav")
			{
				commandText = "SELECT software.*, softwarelist.name AS softwarelist_name, COUNT() OVER() AS ao_total FROM softwarelist INNER JOIN software ON softwarelist.softwarelist_Id = software.softwarelist_Id " +
					"WHERE ((@FAVORITES) @SEARCH) ORDER BY software.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET";

				string[][] listSoftwareNames = Globals.Favorites.ListSoftwareUsedByMachine(favorites_machine);

				if (listSoftwareNames.Length == 0)
				{
					commandText = commandText.Replace("@FAVORITES", "software_id = -1");
				}
				else
				{
					StringBuilder text = new StringBuilder();
					foreach (string[] listSoftwareName in listSoftwareNames)
					{
						if (text.Length > 0)
							text.Append(" OR ");

						text.Append($"(softwarelist.name = '{listSoftwareName[0]}' AND software.name = '{listSoftwareName[1]}')");
					}
					commandText = commandText.Replace("@FAVORITES", text.ToString());
				}
			}

			if (search == null)
			{
				commandText = commandText.Replace("@SEARCH", "");
			}
			else
			{
				search = "%" + String.Join("%", search.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + "%";
				commandText = commandText.Replace("@SEARCH",
					" AND (software.name LIKE @name OR software.description LIKE @description)");
			}

			commandText = commandText.Replace("@LIMIT", limit.ToString());
			commandText = commandText.Replace("@OFFSET", offset.ToString());

			SqlCommand command = new SqlCommand(commandText, _SoftwareConnection);

			if (search != null)
			{
				command.Parameters.AddWithValue("@name", search);
				command.Parameters.AddWithValue("@description", search);
			}

			DataTable table = ExecuteFill(command);

			if (favorites_machine != null)
				Globals.Favorites.AddColumnSoftware(table, favorites_machine, softwareListName, "name", "favorite");

			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow GetSoftwareList(string name)
		{
			DataTable table = ExecuteFill(_SoftwareConnection, $"SELECT * FROM softwarelist WHERE name = '{name}'");
			if (table.Rows.Count == 0)
				return null;
			return table.Rows[0];
		}

		public DataRow[] GetSoftwareRoms(DataRow software)
		{
			long software_id = (long)software["software_id"];
			DataTable table = ExecuteFill(_SoftwareConnection,
				"SELECT rom.* FROM (part INNER JOIN dataarea ON part.part_id = dataarea.part_id) INNER JOIN rom ON dataarea.dataarea_id = rom.dataarea_id " +
				$"WHERE part.software_id = {software_id} AND rom.[name] IS NOT NULL AND rom.[sha1] IS NOT NULL");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetSoftwareDisks(DataRow software)
		{
			long software_id = (long)software["software_id"];
			DataTable table = ExecuteFill(_SoftwareConnection,
				"SELECT disk.* FROM (part INNER JOIN diskarea ON part.part_id = diskarea.part_id) INNER JOIN disk ON diskarea.diskarea_id = disk.diskarea_id " +
				$"WHERE part.software_id = {software_id} AND disk.[name] IS NOT NULL AND disk.[sha1] IS NOT NULL");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataRow[] GetSoftwareSharedFeats(DataRow software)
		{
			long software_id = (long)software["software_id"];
			DataTable table = ExecuteFill(_SoftwareConnection, $"SELECT * FROM sharedfeat WHERE software_id = {software_id}");
			return table.Rows.Cast<DataRow>().ToArray();
		}

		public DataQueryProfile GetDataQueryProfile(string key)
		{
			DataQueryProfile found = null;

			if (key.StartsWith("genre") == true)
			{
				long genre_id = Int64.Parse(key.Split(new char[] { '-' })[1]);

				found = new DataQueryProfile() {
					Key = key,
					Text = "genre",
					Decription = "genre",
					CommandText =
					"SELECT machine.*, driver.*, COUNT() OVER() AS ao_total " +
					"FROM machine INNER JOIN driver ON machine.machine_id = driver.machine_id " +
					"WHERE ((genre_id = @genre_id) @SEARCH) " +
					"ORDER BY machine.description COLLATE NOCASE ASC " +
					"LIMIT @LIMIT OFFSET @OFFSET",
				};

				found.CommandText = found.CommandText.Replace("@genre_id", genre_id.ToString());
			}
			else
			{
				foreach (DataQueryProfile profile in Database.DataQueryProfiles)
				{
					if (profile.Key == key)
					{
						found = profile;
						break;
					}
				}
			}

			if (found == null)
				throw new ApplicationException($"Data Profile not found {key}");

			return found;
		}

		public DataTable QueryMachine(string key, int offset, int limit, string search)
		{
			DataQueryProfile profile = GetDataQueryProfile(key);

			string commandText = profile.CommandText;
		
			if (search == null)
			{
				commandText = commandText.Replace("@SEARCH", "");
			}
			else
			{
				search = "%" + String.Join("%", search.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + "%";
				commandText = commandText.Replace("@SEARCH",
					" AND (machine.name LIKE @name OR machine.description LIKE @description)");
			}

			if (profile.Key == "favorites")
			{
				string favorites = "machine.machine_id = -1";
				if (Globals.Favorites._Machines.Count > 0)
				{
					StringBuilder text = new StringBuilder();
					foreach (string name in Globals.Favorites._Machines.Keys)
					{
						if (text.Length > 0)
							text.Append(" OR ");
						text.Append($"(name = '{name}')");
					}
					favorites = text.ToString();
				}
				commandText = commandText.Replace("@FAVORITES", $" AND ({favorites})");
			}

			commandText = commandText.Replace("@LIMIT", limit.ToString());
			commandText = commandText.Replace("@OFFSET", offset.ToString());

			SqlCommand command = new SqlCommand(commandText, _MachineConnection);

			if (search != null)
			{
				command.Parameters.AddWithValue("@name", search);
				command.Parameters.AddWithValue("@description", search);
			}

			DataTable table = ExecuteFill(command);

			Globals.Favorites.AddColumnMachines(table, "name", "favorite");

			return table;
		}

		public HashSet<string> GetAllSHA1s()
		{
			HashSet<string> result = new HashSet<string>();

			foreach (SqlConnection connection in new SqlConnection[] { _MachineConnection, _SoftwareConnection })
			{
				foreach (string tableName in new string[] { "rom", "disk" })
				{
					DataTable table = ExecuteFill(connection, $"SELECT \"sha1\" FROM \"{tableName}\" WHERE \"sha1\" IS NOT NULL");
					foreach (DataRow row in table.Rows)
						result.Add((string)row["sha1"]);
				}
			}
			
			return result;
		}

		public static SqlConnection DatabaseFromXML(string xmlFilename, string sqliteFilename, string assemblyVersion)
		{
			string connectionString = $"Data Source='{sqliteFilename}';datetimeformat=CurrentCulture;";

            SqlConnection connection = new SqlConnection(connectionString);

			if (File.Exists(sqliteFilename) == true)
			{
				if (TableExists(connection, "ao_info") == true)
				{
					DataTable aoInfoTable = ExecuteFill(connection, "SELECT * FROM ao_info");
					if (aoInfoTable.Rows.Count == 1)
					{
						string dbAssemblyVersion = (string)aoInfoTable.Rows[0]["assembly_version"];
						if (dbAssemblyVersion == assemblyVersion)
							return connection;
					}
				}
				Console.WriteLine("Existing SQLite database is old version re-creating: " + sqliteFilename);
			}

			if (File.Exists(sqliteFilename) == true)
				File.Delete(sqliteFilename);

			File.WriteAllBytes(sqliteFilename, new byte[0]);

			Console.Write($"Loading XML {xmlFilename} ...");
			XElement document = XElement.Load(xmlFilename);
			Console.WriteLine("...done.");

			Console.Write($"Importing XML {document.Name.LocalName} ...");
			DataSet dataSet = ReadXML.ImportXML(document);
			Console.WriteLine("...done.");

			Console.Write($"Adding extra data columns {document.Name.LocalName} ...");
			AddDataExtras(dataSet, document.Name.LocalName, assemblyVersion);
			Console.WriteLine("...done.");

			DatabaseFromXML(document, connection, dataSet);

			return connection;
		}
		public static SqlConnection DatabaseFromXML(XElement document, SqlConnection connection, DataSet dataSet)
		{
			Console.Write($"Creating SQLite {document.Name.LocalName} ...");
			connection.Open();
			try
			{
				foreach (DataTable table in dataSet.Tables)
				{
					List<string> columnDefinitions = new List<string>();

					foreach (DataColumn column in table.Columns)
					{
						string dataType = "TEXT";
						if (column.ColumnName.EndsWith("_id") == true)
						{
							dataType = columnDefinitions.Count == 0 ? "INTEGER PRIMARY KEY" : "INTEGER";
						}
						else
						{
							if (column.DataType == typeof(int) || column.DataType == typeof(long))
								dataType = "INTEGER";
						}

						if (table.TableName == "machine" && column.ColumnName == "description")
							dataType += " COLLATE NOCASE";

						columnDefinitions.Add($"\"{column.ColumnName}\" {dataType}");
					}

					string tableDefinition = $"CREATE TABLE {table.TableName}({String.Join(",", columnDefinitions.ToArray())});";

					using (SqlCommand command = new SqlCommand(tableDefinition, connection))
					{
						command.ExecuteNonQuery();
					}
				}

				foreach (DataTable table in dataSet.Tables)
				{
					Console.Write($"{table.TableName}...");

					List<string> columnNames = new List<string>();
					List<string> parameterNames = new List<string>();
					foreach (DataColumn column in table.Columns)
					{
						columnNames.Add($"\"{column.ColumnName}\"");
						parameterNames.Add("@" + column.ColumnName);
					}

					string commandText = $"INSERT INTO {table.TableName}({String.Join(",", columnNames.ToArray())}) VALUES({String.Join(",", parameterNames.ToArray())});";

					SqlTransaction transaction = connection.BeginTransaction();
					try
					{
						foreach (DataRow row in table.Rows)
						{
							using (SqlCommand command = new SqlCommand(commandText, connection, transaction))
							{
								foreach (DataColumn column in table.Columns)
									command.Parameters.AddWithValue("@" + column.ColumnName, row[column]);

								command.ExecuteNonQuery();
							}
						}

						transaction.Commit();
					}
					catch
					{
						transaction.Rollback();
						throw;
					}
				}

				if (document.Name.LocalName == "mame")
				{
					foreach (string commandText in new string[] {
						"CREATE INDEX machine_name_index ON machine(name);"
						})
						using (SqlCommand command = new SqlCommand(commandText, connection))
							command.ExecuteNonQuery();
				}

				if (document.Name.LocalName == "softwarelists")
				{

				}

			}
			finally
			{
				connection.Close();
			}
			Console.WriteLine("...done.");

			return connection;
		}

		public static bool TableExists(SqlConnection connection, string tableName)
		{
			object obj = ExecuteScalar(connection, $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'");

			if (obj == null || obj is DBNull)
				return false;

			return true;
		}
		public static string[] TableList(SQLiteConnection connection)
		{
			List<string> tableNames = new List<string>();

			DataTable table = Database.ExecuteFill(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
			foreach (DataRow row in table.Rows)
			{
				string tableName = (string)row[0];

				if (tableName.StartsWith("sqlite_") == false)
					tableNames.Add(tableName);
			}
			return tableNames.ToArray();
		}
		public static object ExecuteScalar(SqlConnection connection, string commandText)
		{
			connection.Open();
			try
			{
				using (SqlCommand command = new SqlCommand(commandText, connection))
					return command.ExecuteScalar();
			}
			finally
			{
				connection.Close();
			}
		}

		//public static int ExecuteNonQuery(SQLiteConnection connection, string commandText)
		//{
		//	connection.Open();
		//	try
		//	{
		//		using (SQLiteCommand command = new SQLiteCommand(commandText, connection))
		//			return command.ExecuteNonQuery();
		//	}
		//	finally
		//	{
		//		connection.Close();
		//	}
		//}

		public static DataTable ExecuteFill(SqlConnection connection, string commandText)
		{
			DataTable table = new DataTable();
			using (SqlDataAdapter adapter = new SqlDataAdapter(commandText, connection))
				adapter.Fill(table);
			return table;
		}
		public static DataTable ExecuteFill(SqlCommand command)
		{
			DataTable table = new DataTable();
			using (SqlDataAdapter adapter = new SqlDataAdapter(command))
				adapter.Fill(table);
			return table;
		}

		//
		// MS SQL
		//

		public static bool DatabaseExists(SqlConnection connection, string databaseName)
		{
			object obj = ExecuteScalar(connection, $"SELECT name FROM sys.databases WHERE name = '{databaseName}'");

			if (obj == null || obj is DBNull)
				return false;

			return true;
		}

		//public static object ExecuteScalar(SQLiteConnection connection, string commandText)
		//{
		//	connection.Open();
		//	try
		//	{
		//		using (SQLiteCommand command = new SQLiteCommand(commandText, connection))
		//			return command.ExecuteScalar();
		//	}
		//	finally
		//	{
		//		connection.Close();
		//	}

		//}

		public static int ExecuteNonQuery(SqlConnection connection, string commandText)
		{
			connection.Open();
			try
			{
				using (SqlCommand command = new SqlCommand(commandText, connection))
					return command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}
		public static DataTable ExecuteFill(SQLiteConnection connection, string commandText)
		{
			DataTable table = new DataTable();
			using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(commandText, connection))
				adapter.Fill(table);
			return table;
		}

		public static void BulkInsert(SqlConnection connection, DataTable table)
		{
			using (SqlBulkCopy sqlBulkCopy = new SqlBulkCopy(connection))
			{
				sqlBulkCopy.DestinationTableName = table.TableName;

				sqlBulkCopy.BulkCopyTimeout = 15 * 60;

				connection.Open();
				try
				{
					sqlBulkCopy.WriteToServer(table);
				}
				finally
				{
					connection.Close();
				}
			}
		}

		public static string[] TableList(SqlConnection connection)
		{
			List<string> result = new List<string>();

			DataTable table = ExecuteFill(connection,
				"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME");

			foreach (DataRow row in table.Rows)
				result.Add((string)row["TABLE_NAME"]);

			return result.ToArray();
		}

		public static void ConsoleQuery(string database, string commandText)
		{
			SqlConnection connection = database == "m" ? Globals.Database._MachineConnection : Globals.Database._SoftwareConnection;

			try
			{
				DataTable table = ExecuteFill(connection, commandText);

				StringBuilder text = new StringBuilder();

				foreach (DataColumn column in table.Columns)
				{
					if (column.Ordinal > 0)
						text.Append('\t');
					text.Append(column.ColumnName);
				}
				Console.WriteLine(text.ToString());

				text.Length = 0;
				foreach (DataColumn column in table.Columns)
				{
					if (column.Ordinal > 0)
						text.Append('\t');
					text.Append(new String('=', column.ColumnName.Length));
				}
				Console.WriteLine(text.ToString());

				foreach (DataRow row in table.Rows)
				{
					text.Length = 0;
					foreach (DataColumn column in table.Columns)
					{
						if (column.Ordinal > 0)
							text.Append('\t');
						if (row.IsNull(column) == false)
							text.Append(Convert.ToString(row[column]));
					}
					Console.WriteLine(text.ToString());
				}
			}
			catch (SQLiteException e)
			{
				Console.WriteLine(e.Message);
			}

			Console.WriteLine();
		}

	}
}
