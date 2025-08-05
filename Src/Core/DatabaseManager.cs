using MySql.Data.MySqlClient;
using Rocket.Core.Logging;
using System;
using System.Data;
using System.Net;
using System.Threading.Tasks;
using Logger = Rocket.Core.Logging.Logger;

namespace URPUnturnov
{
    public class DatabaseManager
    {
        private readonly string connectionString;
        private static DatabaseManager instance;

        public static DatabaseManager Instance
        {
            get
            {
                if (instance == null)
                {
                    throw new InvalidOperationException("Db needs to be loaded before use");
                }
                return instance;
            }
        }

        private DatabaseManager(string server, string database, string username, string password, int port)
        {
            connectionString = $"Server={server};Database={database};Uid={username};Pwd={password};Port={port};SslMode=none;Charset=utf8mb4;ConnectionTimeout=30;DefaultCommandTimeout=30;";
        }

        public static void Initialize(string server, string database, string username, string password, int port)
        {
            instance = new DatabaseManager(server, database, username, password, port);
            instance.InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using (var connection = GetConnection())
                {
                    Logger.Log("Loading database");
                    connection.Open();
                    Logger.Log($"Db state : {connection.State}");
                    
                }

                MainClass.SendMainWebhook("Db Connected");
            }
            catch (MySqlException mysqlEx)
            {
                string errorMessage = GetMySqlErrorDescription(mysqlEx.Number);
                Logger.LogError($"MySQL Error {mysqlEx.Number}: {mysqlEx.Message}");
                Logger.LogError($"Error Description: {errorMessage}");
                Logger.LogError($"Full exception: {mysqlEx}");
                MainClass.SendMainWebhook($"DB Error : {mysqlEx.Number}: {errorMessage}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize database: {ex}");
                MainClass.SendMainWebhook($"**DATABASE ERROR** - Failed to connect: {ex.Message}");
            }
        }

        private string GetSafeConnectionString()
        {
            return connectionString.Replace($"Pwd={connectionString.Split(';')[3].Split('=')[1]}", "Pwd=***");
        }

        private string GetMySqlErrorDescription(int errorNumber)
        {
            switch (errorNumber)
            {
                case 0:
                    return "Unable to connect to any of the specified MySQL hosts";
                case 1042:
                    return "Unable to connect to MySQL server (network unreachable)";
                case 1043:
                    return "Bad handshake from MySQL server";
                case 1044:
                    return "Access denied for database";
                case 1045:
                    return "Access denied for user (using password: YES/NO)";
                case 1049:
                    return "Unknown database";
                case 1130:
                    return "Host is not allowed to connect to this MySQL server";
                case 2002:
                    return "Can't connect to local MySQL server through socket";
                case 2003:
                    return "Can't connect to MySQL server (connection refused)";
                case 2005:
                    return "Unknown MySQL server host";
                case 2013:
                    return "Lost connection to MySQL server during query";
                case 2026:
                    return "SSL connection error";
                default:
                    return "Unknown MySQL error";
            }
        }

        private MySqlConnection GetConnection()
        {
            return new MySqlConnection(connectionString);
        }
        
        public int ExecuteNonQuery(string query, params MySqlParameter[] parameters)
        {
            try
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }
                        return command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"ExecuteNonQuery failed: {ex}");
                return -1;
            }
        }

        public object ExecuteScalar(string query, params MySqlParameter[] parameters)
        {
            try
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }
                        return command.ExecuteScalar();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"ExecuteScalar failed: {ex}");
                return null;
            }
        }

        public DataTable ExecuteQuery(string query, params MySqlParameter[] parameters)
        {
            try
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }

                        using (var adapter = new MySqlDataAdapter(command))
                        {
                            DataTable dataTable = new DataTable();
                            adapter.Fill(dataTable);
                            return dataTable;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"ExecuteQuery failed: {ex}");
                return new DataTable();
            }
        }

        public bool TestConnection()
        {
            try
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    return connection.State == ConnectionState.Open;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Database connection test failed: {ex}");
                return false;
            }
        }
    }
}