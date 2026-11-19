using MySqlConnector;

namespace tiendaDiscos.Data
{
    public static class DatabaseSchemaInitializer
    {
        private static readonly (string Table, string Column)[] SoftDeleteColumns =
        {
            ("Productos", "Activo"),
            ("Proveedores", "Activo")
        };

        public static async Task EnsureSoftDeleteColumnsAsync(string connectionString)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            foreach (var (table, column) in SoftDeleteColumns)
            {
                await using var check = new MySqlCommand(@"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = @table
                      AND COLUMN_NAME = @column", connection);
                check.Parameters.AddWithValue("@table", table);
                check.Parameters.AddWithValue("@column", column);

                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
                {
                    await using var alter = new MySqlCommand(
                        $"ALTER TABLE `{table}` ADD COLUMN `{column}` TINYINT(1) NOT NULL DEFAULT 1",
                        connection);
                    await alter.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
