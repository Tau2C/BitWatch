using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        }

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public void SetupDatabase()
        {
            FileLogger.Instance.Info("Setting up database...");
            using var connection = GetConnection();
            var script = File.ReadAllText("db_setup.sql");
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
            using var connection = GetConnection();
            connection.Execute("INSERT INTO excluded_nodes (path_id, relative_path) VALUES (@pathId, @relativePath) ON CONFLICT (path_id, relative_path) DO NOTHING", new { pathId, relativePath });
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
    }
}
