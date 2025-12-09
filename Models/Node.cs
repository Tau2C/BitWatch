using System;

namespace BitWatch.Models
{
    public class Node
    {
        public int Id { get; set; }
        public int PathId { get; set; }
        public string RelativePath { get; set; } = "";
        public string Type { get; set; } = ""; // "file" or "directory"
        public string? Hash { get; set; }
        public string? HashAlgorithm { get; set; }
        public DateTime? LastChecked { get; set; }
    }
}
