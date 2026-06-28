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
                    Logger.Log("Connecting to database...");
                    connection.Open();
                    Logger.Log($"DB connection state: {connection.State}");
                    EnsureMarketTable(connection);
                }
            }
            catch (MySqlException mysqlEx)
            {
                string errorMessage = GetMySqlErrorDescription(mysqlEx.Number);
                Logger.LogError($"MySQL Error [{mysqlEx.Number}]: {mysqlEx.Message}");
                Logger.LogError($"Details: {errorMessage}");
                Logger.LogError($"Exception trace: {mysqlEx}");
                MainClass.SendMainWebhook($"DB Error : {mysqlEx.Number}: {errorMessage}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"DB setup failed: {ex}");
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






        public void EnsureMarketTable(MySqlConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS urp_flea_listings (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    item_id SMALLINT UNSIGNED NOT NULL,
                    item_name VARCHAR(255) NOT NULL,
                    category VARCHAR(32) NOT NULL,
                    price DECIMAL(20,2) NOT NULL,
                    seller_name VARCHAR(255) NOT NULL,
                    seller_id BIGINT UNSIGNED NOT NULL,
                    quality TINYINT UNSIGNED NOT NULL DEFAULT 0,
                    state BLOB NULL,
                    quantity TINYINT UNSIGNED NOT NULL DEFAULT 1,
                    listed_at DATETIME(6) NOT NULL,
                    status VARCHAR(20) NOT NULL DEFAULT 'active',
                    completed_at DATETIME(6) NULL,
                    expires_at DATETIME(6) NOT NULL,
                    expiry_logged_at DATETIME(6) NULL,
                    INDEX idx_seller (seller_id),
                    INDEX idx_category (category),
                    INDEX idx_listed (listed_at),
                    INDEX idx_status (status)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            using (var cmd = new MySqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
            MigrateListingsTableAddStatus(connection);
            MigrateListingsTableAddExpiresAt(connection);
            MigrateListingsTableAddExpiryLoggedAt(connection);
            EnsureTransactionsTable(connection);
            EnsureStatsTable(connection);
            Logger.Log("Market tables verified.");
        }




        private void MigrateListingsTableAddStatus(MySqlConnection connection)
        {
            try
            {
                using (var check = new MySqlCommand("SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'urp_flea_listings' AND COLUMN_NAME = 'status' LIMIT 1", connection))
                {
                    if (check.ExecuteScalar() != null) return;
                }
                const string alterSql = @"ALTER TABLE urp_flea_listings
                    ADD COLUMN status VARCHAR(20) NOT NULL DEFAULT 'active',
                    ADD COLUMN completed_at DATETIME(6) NULL,
                    ADD INDEX idx_status (status)";
                using (var cmd = new MySqlCommand(alterSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }
                Logger.Log("Applied status column migrations.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Status migration error: {ex.Message}");
            }
        }




        private void MigrateListingsTableAddExpiresAt(MySqlConnection connection)
        {
            try
            {
                using (var check = new MySqlCommand("SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'urp_flea_listings' AND COLUMN_NAME = 'expires_at' LIMIT 1", connection))
                {
                    if (check.ExecuteScalar() != null) return;
                }
                const string alterSql = @"ALTER TABLE urp_flea_listings ADD COLUMN expires_at DATETIME(6) NULL";
                using (var cmd = new MySqlCommand(alterSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }
                using (var update = new MySqlCommand("UPDATE urp_flea_listings SET expires_at = DATE_ADD(listed_at, INTERVAL 7 DAY) WHERE expires_at IS NULL", connection))
                {
                    update.ExecuteNonQuery();
                }
                const string notNullSql = @"ALTER TABLE urp_flea_listings MODIFY COLUMN expires_at DATETIME(6) NOT NULL";
                using (var cmd = new MySqlCommand(notNullSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }
                Logger.Log("Applied expiration column migrations.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Expiration migration error: {ex.Message}");
            }
        }




        private void MigrateListingsTableAddExpiryLoggedAt(MySqlConnection connection)
        {
            try
            {
                using (var check = new MySqlCommand("SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'urp_flea_listings' AND COLUMN_NAME = 'expiry_logged_at' LIMIT 1", connection))
                {
                    if (check.ExecuteScalar() != null) return;
                }
                const string alterSql = @"ALTER TABLE urp_flea_listings ADD COLUMN expiry_logged_at DATETIME(6) NULL";
                using (var cmd = new MySqlCommand(alterSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }
                Logger.Log("Applied log column migrations.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Log migration error: {ex.Message}");
            }
        }




        private void EnsureTransactionsTable(MySqlConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS urp_flea_transactions (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    listing_id INT NOT NULL,
                    buyer_steam_id BIGINT UNSIGNED NOT NULL,
                    buyer_name VARCHAR(255) NOT NULL,
                    seller_steam_id BIGINT UNSIGNED NOT NULL,
                    seller_name VARCHAR(255) NOT NULL,
                    item_id SMALLINT UNSIGNED NOT NULL,
                    item_name VARCHAR(255) NOT NULL,
                    quantity TINYINT UNSIGNED NOT NULL,
                    total_paid DECIMAL(20,2) NOT NULL,
                    fee_amount DECIMAL(20,2) NOT NULL,
                    seller_payout DECIMAL(20,2) NOT NULL,
                    created_at DATETIME(6) NOT NULL,
                    INDEX idx_listing (listing_id),
                    INDEX idx_buyer (buyer_steam_id),
                    INDEX idx_seller (seller_steam_id),
                    INDEX idx_created (created_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            using (var cmd = new MySqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }




        public void RecordTransaction(int listingId, ulong buyerSteamId, string buyerName, ulong sellerSteamId, string sellerName,
            ushort itemId, string itemName, byte quantity, decimal totalPaid, decimal feeAmount, decimal sellerPayout)
        {
            const string sql = @"INSERT INTO urp_flea_transactions (listing_id, buyer_steam_id, buyer_name, seller_steam_id, seller_name, item_id, item_name, quantity, total_paid, fee_amount, seller_payout, created_at)
                VALUES (@listing_id, @buyer_steam_id, @buyer_name, @seller_steam_id, @seller_name, @item_id, @item_name, @quantity, @total_paid, @fee_amount, @seller_payout, @created_at)";
            var parameters = new[]
            {
                new MySqlParameter("@listing_id", listingId),
                new MySqlParameter("@buyer_steam_id", buyerSteamId),
                new MySqlParameter("@buyer_name", buyerName ?? ""),
                new MySqlParameter("@seller_steam_id", sellerSteamId),
                new MySqlParameter("@seller_name", sellerName ?? ""),
                new MySqlParameter("@item_id", itemId),
                new MySqlParameter("@item_name", itemName ?? ""),
                new MySqlParameter("@quantity", quantity),
                new MySqlParameter("@total_paid", totalPaid),
                new MySqlParameter("@fee_amount", feeAmount),
                new MySqlParameter("@seller_payout", sellerPayout),
                new MySqlParameter("@created_at", DateTime.UtcNow)
            };
            ExecuteNonQuery(sql, parameters);
        }




        private void EnsureStatsTable(MySqlConnection connection)
        {
            const string createSql = @"
                CREATE TABLE IF NOT EXISTS urp_flea_stats (
                    id TINYINT PRIMARY KEY DEFAULT 1,
                    total_taxed_money DECIMAL(24,2) NOT NULL DEFAULT 0
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            using (var cmd = new MySqlCommand(createSql, connection))
            {
                cmd.ExecuteNonQuery();
            }
            const string ensureRowSql = "INSERT IGNORE INTO urp_flea_stats (id, total_taxed_money) VALUES (1, 0)";
            using (var cmd = new MySqlCommand(ensureRowSql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }




        public void AddTotalTaxedMoney(decimal amount)
        {
            if (amount <= 0) return;
            const string sql = "UPDATE urp_flea_stats SET total_taxed_money = total_taxed_money + @amount WHERE id = 1";
            ExecuteNonQuery(sql, new MySqlParameter("@amount", amount));
        }




        public int ExecuteInsertAndReturnId(string insertSql, params MySqlParameter[] parameters)
        {
            try
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    using (var command = new MySqlCommand(insertSql, connection))
                    {
                        if (parameters != null)
                            command.Parameters.AddRange(parameters);
                        command.ExecuteNonQuery();
                    }
                    using (var command = new MySqlCommand("SELECT LAST_INSERT_ID()", connection))
                    {
                        var result = command.ExecuteScalar();
                        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Insert query failed: {ex}");
                return -1;
            }
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
                Logger.LogError($"Query execution failed: {ex}");
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
                Logger.LogError($"DB ping failed: {ex}");
                return false;
            }
        }
    }
}