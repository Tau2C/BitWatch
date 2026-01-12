using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BitWatch.Models;
using Dapper;
using Npgsql;

namespace BitWatch.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
            SetupDatabase();

            // Explicit Dapper mapping for Node
            SqlMapper.SetTypeMap(
                typeof(Node),
                new CustomPropertyTypeMap(
                    typeof(Node),
                    (type, columnName) =>
                    {
                        return type.GetProperties().FirstOrDefault(prop =>
                            string.Equals(prop.Name, columnName, StringComparison.OrdinalIgnoreCase) ||
                            (prop.Name == "PathId" && columnName == "path_id") ||
                            (prop.Name == "RelativePath" && columnName == "relative_path") ||
                            (prop.Name == "HashAlgorithm" && columnName == "hash_algorithm") ||
                            (prop.Name == "LastChecked" && columnName == "last_checked"))!;
                    }
            ));

            // Explicit Dapper mapping for ExcludedNode
            SqlMapper.SetTypeMap(
                typeof(ExcludedNode),
                new CustomPropertyTypeMap(
                    typeof(ExcludedNode),
                    (type, columnName) =>
                    {
                        return type.GetProperties().FirstOrDefault(prop =>
                            string.Equals(prop.Name, columnName, StringComparison.OrdinalIgnoreCase) ||
                            (prop.Name == "PathId" && columnName == "path_id") ||
                            (prop.Name == "RelativePath" && columnName == "relative_path"))!;
                    })
                );
        }

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public void SetupDatabase()
        {
            FileLogger.Instance.Info("Setting up database...");
            using var connection = GetConnection();
            var baseDirectory = AppContext.BaseDirectory;
            var sqlFilePath = Path.Combine(baseDirectory, "db_setup.sql");
            var script = File.ReadAllText(sqlFilePath);
            connection.Execute(script);
        }

        public void AddPathToScan(string path)
        {
            using var connection = GetConnection();
            connection.Execute("INSERT INTO paths_to_scan (path) VALUES (@path) ON CONFLICT (path) DO NOTHING", new { path });
        }

        public IEnumerable<string> GetPathsToScan()
        {
            using var connection = GetConnection();
            return connection.Query<string>("SELECT path FROM paths_to_scan");
        }

        public void RemovePathToScan(string path)
        {
            using var connection = GetConnection();
            connection.Execute("DELETE FROM paths_to_scan WHERE path = @path", new { path });
        }

        public int GetPathId(string path)
        {
            using var connection = GetConnection();
            return connection.QuerySingleOrDefault<int>("SELECT id FROM paths_to_scan WHERE path = @path", new { path });
        }

        public Node? GetNodeByRelativePath(int pathId, string relativePath)
        {
            using var connection = GetConnection();
            return connection.Query<Node>("SELECT * FROM nodes WHERE path_id = @pathId AND relative_path = @relativePath", new { pathId, relativePath }).FirstOrDefault();
        }

        public void UpsertNode(Node node)
        {
            using var connection = GetConnection();
            var existingNode = GetNodeByRelativePath(node.PathId, node.RelativePath);
            if (existingNode != null)
            {
                node.Id = existingNode.Id;
                connection.Execute(
                    "UPDATE nodes SET hash = @Hash, hash_algorithm = @HashAlgorithm, last_checked = @LastChecked WHERE id = @Id",
                    node);
            }
            else
            {
                connection.Execute(
                    "INSERT INTO nodes (path_id, relative_path, type, hash, hash_algorithm, last_checked) VALUES (@PathId, @RelativePath, @Type, @Hash, @HashAlgorithm, @LastChecked)",
                    node);
            }
        }

        public void AddExcludedNode(int pathId, string relativePath)
        {
            FileLogger.Instance.Debug($"Calling add_and_clean_excluded_node: pathId={pathId}, relativePath='{relativePath}'");
            using var connection = GetConnection();
            connection.Execute("SELECT add_and_clean_excluded_node(@p_path_id, @p_relative_path)", new { p_path_id = pathId, p_relative_path = relativePath });
        }

        public IEnumerable<Node> GetNodesForPath(int pathId)
        {
            using var connection = GetConnection();
            return connection.Query<Node>("SELECT * FROM nodes WHERE path_id = @pathId", new { pathId });
        }

        public void RemoveNodes(List<Node> nodesToRemove)
        {
            if (!nodesToRemove.Any()) return;

            FileLogger.Instance.Info($"Removing {nodesToRemove.Count} nodes from database.");
            using var connection = GetConnection();
            // Dapper can execute a single DELETE statement for multiple items efficiently
            connection.Execute("DELETE FROM nodes WHERE id = @Id", nodesToRemove);
        }

        public IEnumerable<ExcludedNode> GetExcludedNodes()
        {
            using var connection = GetConnection();
            return connection.Query<ExcludedNode>("SELECT * FROM excluded_nodes");
        }

        public void RemoveExcludedNode(int id)
        {
            using var connection = GetConnection();
            connection.Execute("DELETE FROM excluded_nodes WHERE id = @id", new { id });
        }

        public string? GetSetting(string key)
        {
            using var connection = GetConnection();
            return connection.QuerySingleOrDefault<string>("SELECT value FROM app_settings WHERE key = @key", new { key });
        }

        public void SaveSetting(string key, string value)
        {
            using var connection = GetConnection();
            connection.Execute("INSERT INTO app_settings (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = @value", new { key, value });
        }
    }
}
